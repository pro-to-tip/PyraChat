﻿namespace Pyratron.PyraChat.IRC.Messages.Receive.Numerics
{
    /// <summary>
    /// Welcome message. (001)
    /// </summary>
    public class WelcomeMessage : ReceivableMessage
    {
        /// <summary>
        /// Welcome text.
        /// </summary>
        public string Text => BaseMessage.Parameters[1];

        public WelcomeMessage(Message msg) : base(msg)
        {
            msg.Client.OnConnect();
            msg.Client.OnReplyWelcome(this);
        }

        public static bool CanProcess(Message msg)
        {
            return msg.Type == "001";
        }
    }
}