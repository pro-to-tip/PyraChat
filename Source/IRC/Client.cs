﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Pyratron.PyraChat.IRC.Messages;
using Pyratron.PyraChat.IRC.Messages.Receive;
using Pyratron.PyraChat.IRC.Messages.Receive.Numerics;
using Pyratron.PyraChat.IRC.Messages.Receive.v3;
using Pyratron.PyraChat.IRC.Messages.Send;
using Pyratron.PyraChat.IRC.Messages.Send.v3;
using AwayMessage = Pyratron.PyraChat.IRC.Messages.Receive.Numerics.AwayMessage;
using InviteMessage = Pyratron.PyraChat.IRC.Messages.Receive.InviteMessage;
using IsOnMessage = Pyratron.PyraChat.IRC.Messages.Receive.Numerics.IsOnMessage;
using JoinMessage = Pyratron.PyraChat.IRC.Messages.Receive.JoinMessage;
using ListMessage = Pyratron.PyraChat.IRC.Messages.Receive.Numerics.ListMessage;
using MOTDMessage = Pyratron.PyraChat.IRC.Messages.Receive.Numerics.MOTDMessage;
using NickMessage = Pyratron.PyraChat.IRC.Messages.Receive.NickMessage;
using OperwallMessage = Pyratron.PyraChat.IRC.Messages.Receive.OperwallMessage;
using PrivateMessage = Pyratron.PyraChat.IRC.Messages.Receive.PrivateMessage;
using QuitMessage = Pyratron.PyraChat.IRC.Messages.Receive.QuitMessage;
using TimeMessage = Pyratron.PyraChat.IRC.Messages.Receive.Numerics.TimeMessage;
using UserModeMessage = Pyratron.PyraChat.IRC.Messages.Receive.UserModeMessage;
using VersionMessage = Pyratron.PyraChat.IRC.Messages.Receive.Numerics.VersionMessage;
using WhoisMessage = Pyratron.PyraChat.IRC.Messages.Receive.Numerics.WhoisMessage;
using WhoMessage = Pyratron.PyraChat.IRC.Messages.Receive.Numerics.WhoMessage;

namespace Pyratron.PyraChat.IRC
{
    public class Client
    {
        /// <summary>
        /// The port of the IRC server.
        /// </summary>
        public int Port { get; }

        /// <summary>
        /// The hostname of the IRC server.
        /// </summary>
        public string Host { get; }

        /// <summary>
        /// The user connected to this network.
        /// </summary>
        public User User { get; }

        /// <summary>
        /// Capabilities supported by the server.
        /// </summary>
        public List<Capability> SupportedCapabilities { get; } = new List<Capability>();

        /// <summary>
        /// Active capabilities. (Acknowledged by server)
        /// </summary>
        public List<Capability> ActiveCapabilities { get; } = new List<Capability>();

        public List<User> Users { get; }
        internal StringBuilder MOTDBuilder { get; set; } = new StringBuilder();
        public List<Channel> Channels { get; }
        private readonly Thread networkThread;
        private readonly TcpClient tcpClient;
        private NetworkStream netStream;
        private StreamReader reader;
        private StreamWriter writer;

        /// <summary>
        /// Creates a new IRC client.
        /// </summary>
        /// <param name="host">Hostname.</param>
        /// <param name="port">Port.</param>
        /// <param name="user">User to connect with.</param>
        public Client(string host, int port, User user)
        {
            Channels = new List<Channel>();
            Users = new List<User> {user};
            Host = host;
            Port = port;
            User = user;

            tcpClient = new TcpClient();
            networkThread = new Thread(ProcessMessages);

            // Register neccessary internal events
            Connect += () =>
            {
                // Send IRCv3 capabilities.
                Send(new CapabilityListSupportedMessage());
                Send(new CapabilityRequestMessage(IRC.Capability.MultiPrefix, IRC.Capability.AwayNotify));
                Send(new CapabilityEndMessage());
            };
            Ping += message => Send(new PongMessage(message.Extra));
            // Request channel modes on channel join.
            ChannelJoin += message => Send(new Messages.Send.ChannelModeMessage(message.Channel));
        }

        /// <summary>
        /// Find a channel by name. If it doesn't exist, it will be created.
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public Channel ChannelFromName(string name)
        {
            var chan = Channels.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            return chan ?? new Channel(this, name);
        }

        /// <summary>
        /// Sends the specified message.
        /// </summary>
        public void Send(SendableMessage message)
        {
            message.Send(writer, this);
        }

        /// <summary>
        /// Connects to the server.
        /// </summary>
        public void Start()
        {
            tcpClient.Connect(Host, Port);
            netStream = tcpClient.GetStream();

            reader = new StreamReader(netStream);
            writer = new StreamWriter(netStream)
            {
                NewLine = "\r\n",
                AutoFlush = true
            };

            networkThread.Start();

            //Send user information
            Send(new UserMessage(User));
            Send(new Messages.Send.NickMessage(User));
        }

        /// <summary>
        /// Returns the user that best matches the mask provided.
        /// If any fields are blank in the user, they will be filled in with data from the mask.
        /// If the user is not found, they will be created.
        /// </summary>
        public User UserFromMask(string mask)
        {
            var match = User.MaskRegex.Match(mask);
            if (!match.Success) return null;

            var user = UserFromNick(match.Groups[1].Value);
            if (user == null)
            {
                user = new User(mask);
                Users.Add(user);
            }
            user.Nick = match.Groups[1].Value;
            user.Ident = match.Groups[2].Value;
            user.Host = match.Groups[3].Value;
            return user;
        }

        /// <summary>
        /// Returns the user whose nickname is equal to the value specified.
        /// </summary>
        public User UserFromNick(string nick)
        {
            //Remove rank from nick
            var rank = UserRank.FromPrefix(nick[0]);
            if (rank != UserRank.None)
                nick = nick.Substring(1);
            return Users.FirstOrDefault(u => u.Nick.Equals(nick, StringComparison.OrdinalIgnoreCase));
        }

        private void ProcessMessages()
        {
            while (tcpClient != null && tcpClient.Connected && reader != null && !reader.EndOfStream)
            {
                var line = reader.ReadLine();
                if (string.IsNullOrEmpty(line)) continue;

                //Parse the message
                var msg = new Message(this, line);
                //Choose a message type and process the message
                msg.Process();
                OnIRCMessage(msg);
            }
        }

        #region Events

        public delegate void IRCMessageEventHandler(Message message);

        public delegate void MessageEventHandler(PrivateMessage message);

        public delegate void PingEventHandler(PingMessage message);

        public delegate void NoticeEventHandler(NoticeMessage message);

        public delegate void ConnectEventHandler();

        public delegate void ReplyWelcomeEventHandler(WelcomeMessage message);

        public delegate void ReplyYourHostEventHandler(YourHostMessage message);

        public delegate void ReplyCreatedEventHandler(CreatedMessage message);

        public delegate void ReplyMyInfoEventHandler(MyInfoMessage message);

        public delegate void ReplyISupportEventHandler(SupportMessage message);

        public delegate void ReplyBounceEventHandler(BounceMessage message);

        public delegate void ReplyMOTDEndEventHandler(MOTDEndMessage message);

        public delegate void ReplyMOTDStartEventHandler(MOTDStartMessage message);

        public delegate void ReplyMOTDEventHandler(MOTDMessage message);

        public delegate void ReplyLUserEventHandler(LUserMessage message);

        public delegate void ReplyNamesEventHandler(NamesMessage message);

        public delegate void ReplyEndOfNamesEventHandler(EndOfNamesMessage message);

        public delegate void ChannelJoinEventHandler(JoinMessage message);

        public delegate void NickEventHandler(NickMessage message);

        public delegate void QuitEventHandler(QuitMessage message);

        public delegate void UserModeEventHandler(UserModeMessage message);

        public delegate void AwayChangeEventHandler(User user, bool away);

        public delegate void RankChangeEventHandler(User user, string channel, UserRank rank);

        public delegate void InviteEventHandler(InviteMessage message);

        public delegate void ReplyListEventHandler(ListMessage message);

        public delegate void ReplyListEndEventHandler(ListEndMessage message);

        public delegate void ReplyUModeIsEventHandler(UModeIsMessage message);

        public delegate void ReplyYoureOperEventHandler(YoureOperMessage message);

        public delegate void ErrorEventHandler(ErrorMessage message);

        public delegate void NumericEventHandler(NumericMessage message);

        public delegate void ReplyAwayEventHandler(AwayMessage message);

        public delegate void ReplyUnAwayEventHandler(UnAwayMessage message);

        public delegate void ReplyNowAwayEventHandler(NowAwayMessage message);

        public delegate void ReplyVersionEventHandler(VersionMessage message);

        public delegate void ReplyTimeEventHandler(TimeMessage message);

        public delegate void ReplyWhoEventHandler(WhoMessage message);

        public delegate void ReplyWhoisEventHandler(WhoisMessage message);

        public delegate void ReplyEndOfWhoEventHandler(EndOfWhoMessage message);

        public delegate void ReplyEndOfWhoisEventHandler(EndOfWhoisMessage message);

        public delegate void ReplyBanListEventHandler(BanListMessage message);

        public delegate void ReplyEndOfBanListEventHandler(EndOfBanListMessage message);

        public delegate void ReplyExceptListEventHandler(ExceptListMessage message);

        public delegate void ReplyEndOfExceptListEventHandler(EndOfExceptListMessage message);

        public delegate void ReplyInviteListEventHandler(InviteListMessage message);

        public delegate void ReplyEndOfInviteListEventHandler(EndOfInviteListMessage message);

        public delegate void ReplyIsOnEventHandler(IsOnMessage message);

        public delegate void OperwallEventHandler(OperwallMessage message);

        public delegate void AwayEventHandler(Messages.Receive.v3.AwayMessage message);

        public delegate void CapabilityEventHandler(CapabilityMessage message);

        /// <summary>
        /// General output logging message.
        /// </summary>
        public event IRCMessageEventHandler IRCMessage;

        /// <summary>
        /// When a message is received.
        /// </summary>
        public event MessageEventHandler Message;

        /// <summary>
        /// When a PING message is received.
        /// </summary>
        public event PingEventHandler Ping;

        public event NoticeEventHandler Notice;

        /// <summary>
        /// When the connection is established.
        /// </summary>
        public event ConnectEventHandler Connect;

        public event ReplyWelcomeEventHandler ReplyWelcome;
        public event ReplyYourHostEventHandler ReplyYourHost;
        public event ReplyCreatedEventHandler ReplyCreated;
        public event ReplyMyInfoEventHandler ReplyMyInfo;
        public event ReplyISupportEventHandler ReplyISupport;
        public event ReplyBounceEventHandler ReplyBounce;
        public event ReplyMOTDEndEventHandler ReplyMOTDEnd;
        public event ReplyMOTDStartEventHandler ReplyMOTDStart;
        public event ReplyMOTDEventHandler ReplyMOTD;
        public event ReplyLUserEventHandler ReplyLUser;
        public event ReplyNamesEventHandler ReplyNames;
        public event ReplyEndOfNamesEventHandler ReplyEndOfNames;
        public event ChannelJoinEventHandler ChannelJoin;
        public event NickEventHandler Nick;
        public event QuitEventHandler Quit;
        public event UserModeEventHandler UserMode;

        /// <summary>
        /// When an IRCv3 CAP message is received.
        /// </summary>
        public event CapabilityEventHandler Capability;

        /// <summary>
        /// When a user's away state is changed, through WHO, AWAY, etc.
        /// </summary>
        public event AwayChangeEventHandler AwayChange;

        /// <summary>
        /// When an IRCv3 AWAY message is recieved.
        /// </summary>
        public event AwayEventHandler Away;

        public event RankChangeEventHandler RankChange;
        public event InviteEventHandler Invite;
        public event ReplyListEventHandler ReplyList;
        public event ReplyListEndEventHandler ReplyListEnd;
        public event ReplyUModeIsEventHandler ReplyUModeIs;
        public event ReplyYoureOperEventHandler ReplyYoureOper;
        public event ReplyAwayEventHandler ReplyAway;
        public event ReplyUnAwayEventHandler ReplyUnAway;
        public event ReplyNowAwayEventHandler ReplyNowAway;
        public event ReplyVersionEventHandler ReplyVersion;
        public event ReplyTimeEventHandler ReplyTime;
        public event ReplyWhoEventHandler ReplyWho;
        public event ReplyWhoisEventHandler ReplyWhois;
        public event ReplyEndOfWhoEventHandler ReplyEndOfWho;
        public event ReplyEndOfWhoisEventHandler ReplyEndOfWhois;
        public event ReplyBanListEventHandler ReplyBanList;
        public event ReplyEndOfBanListEventHandler ReplyEndOfBanList;
        public event ReplyInviteListEventHandler ReplyInviteList;
        public event ReplyEndOfInviteListEventHandler ReplyEndOfInviteList;
        public event ReplyExceptListEventHandler ReplyExceptList;
        public event ReplyEndOfExceptListEventHandler ReplyEndOfExceptList;
        public event ReplyIsOnEventHandler ReplyIsOn;
        public event OperwallEventHandler Operwall;

        /// <summary>
        /// When an error message (400-599) is received.
        /// </summary>
        public event ErrorEventHandler ErrorMessage;


        /// <summary>
        /// When any numeric message (0-399) is received.
        /// </summary>
        /// <remarks>
        /// Some numeric messages also have their own Reply____ handlers.
        /// </remarks>
        public event NumericEventHandler NumericMessage;

        internal void OnIRCMessage(Message message) => IRCMessage?.Invoke(message);

        internal void OnMessage(PrivateMessage message) => Message?.Invoke(message);
        internal void OnPing(PingMessage message) => Ping?.Invoke(message);
        internal void OnNotice(NoticeMessage message) => Notice?.Invoke(message);
        internal void OnConnect() => Connect?.Invoke();
        internal void OnReplyWelcome(WelcomeMessage message) => ReplyWelcome?.Invoke(message);
        internal void OnReplyYourHost(YourHostMessage message) => ReplyYourHost?.Invoke(message);
        internal void OnReplyCreated(CreatedMessage message) => ReplyCreated?.Invoke(message);
        internal void OnReplyMyInfo(MyInfoMessage message) => ReplyMyInfo?.Invoke(message);
        internal void OnReplyISupport(SupportMessage message) => ReplyISupport?.Invoke(message);
        internal void OnReplyBounce(BounceMessage message) => ReplyBounce?.Invoke(message);
        internal void OnReplyMOTDEnd(MOTDEndMessage message) => ReplyMOTDEnd?.Invoke(message);
        internal void OnReplyMOTDStart(MOTDStartMessage message) => ReplyMOTDStart?.Invoke(message);
        internal void OnReplyMOTD(MOTDMessage message) => ReplyMOTD?.Invoke(message);
        internal void OnReplyLUser(LUserMessage message) => ReplyLUser?.Invoke(message);
        internal void OnReplyNames(NamesMessage message) => ReplyNames?.Invoke(message);
        internal void OnReplyEndOfNames(EndOfNamesMessage message) => ReplyEndOfNames?.Invoke(message);
        internal void OnChannelJoin(JoinMessage message) => ChannelJoin?.Invoke(message);
        internal void OnNick(NickMessage message) => Nick?.Invoke(message);
        internal void OnQuit(QuitMessage message) => Quit?.Invoke(message);
        internal void OnUserMode(UserModeMessage message) => UserMode?.Invoke(message);
        internal void OnAwayChange(User user, bool away) => AwayChange?.Invoke(user, away);
        internal void OnRankChange(User user, string channel, UserRank rank) => RankChange?.Invoke(user, channel, rank);
        internal void OnInvite(InviteMessage message) => Invite?.Invoke(message);
        internal void OnReplyList(ListMessage message) => ReplyList?.Invoke(message);
        internal void OnReplyListEnd(ListEndMessage message) => ReplyListEnd?.Invoke(message);
        internal void OnReplyUModeIs(UModeIsMessage message) => ReplyUModeIs?.Invoke(message);
        internal void OnReplyYoureOper(YoureOperMessage message) => ReplyYoureOper?.Invoke(message);
        internal void OnError(ErrorMessage message) => ErrorMessage?.Invoke(message);
        internal void OnNumeric(NumericMessage message) => NumericMessage?.Invoke(message);
        internal void OnAway(Messages.Receive.v3.AwayMessage message) => Away?.Invoke(message);
        internal void OnReplyAway(AwayMessage message) => ReplyAway?.Invoke(message);
        internal void OnReplyUnAway(UnAwayMessage message) => ReplyUnAway?.Invoke(message);
        internal void OnReplyNowAway(NowAwayMessage message) => ReplyNowAway?.Invoke(message);
        internal void OnReplyVersion(VersionMessage message) => ReplyVersion?.Invoke(message);
        internal void OnReplyTime(TimeMessage message) => ReplyTime?.Invoke(message);
        internal void OnReplyWho(WhoMessage message) => ReplyWho?.Invoke(message);
        internal void OnReplyWhois(WhoisMessage message) => ReplyWhois?.Invoke(message);
        internal void OnReplyEndOfWho(EndOfWhoMessage message) => ReplyEndOfWho?.Invoke(message);
        internal void OnReplyEndOfWhois(EndOfWhoisMessage message) => ReplyEndOfWhois?.Invoke(message);
        internal void OnReplyBanList(BanListMessage message) => ReplyBanList?.Invoke(message);
        internal void OnReplyEndOfBanList(EndOfBanListMessage message) => ReplyEndOfBanList?.Invoke(message);
        internal void OnReplyInviteList(InviteListMessage message) => ReplyInviteList?.Invoke(message);
        internal void OnReplyEndOfInviteList(EndOfInviteListMessage message) => ReplyEndOfInviteList?.Invoke(message);
        internal void OnReplyExceptList(ExceptListMessage message) => ReplyExceptList?.Invoke(message);
        internal void OnReplyEndOfExceptList(EndOfExceptListMessage message) => ReplyEndOfExceptList?.Invoke(message);
        internal void OnReplyIsOn(IsOnMessage message) => ReplyIsOn?.Invoke(message);
        internal void OnOperwall(OperwallMessage message) => Operwall?.Invoke(message);
        internal void OnCapability(CapabilityMessage message) => Capability?.Invoke(message);

        #endregion //Events
    }
}