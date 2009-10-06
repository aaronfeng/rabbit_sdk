namespace RabbitMQ.Client.MessagePatterns.Unicast {

    using Address   = System.String;
    using MessageId = System.String;
    using Name      = System.String;

    public delegate void MessageEventHandler(IMessaging sender, IMessage m);

    public interface IMessage {
        IBasicProperties Properties { get; set; }
        byte[]           Body       { get; set; }
        string           RoutingKey { get; set; }

        Address   From          { get; set; }
        Address   To            { get; set; }
        Address   ReplyTo       { get; set; }
        MessageId MessageId     { get; set; }
        MessageId CorrelationId { get; set; }

        IMessage CreateReply();
    }

    public interface IReceivedMessage : IMessage {
        bool Redelivered { get; }
    }

    public delegate void ConnectionDelegate(IConnection conn);
    public delegate void Thunk();

    public interface IConnector : System.IDisposable {
        void Connect(ConnectionDelegate d);
        bool Reconnect(ConnectionDelegate d);
        bool Try(Thunk t, ConnectionDelegate d);
        void Close();
    }

    public delegate void SetupDelegate(IMessaging m, IModel send, IModel recv);

    public interface IMessaging {

        event MessageEventHandler Sent;

        Address Identity     { get; set; }
        Name    ExchangeName { get; set; }
        Name    QueueName    { get; set; }

        IConnector      Connector     { get; set; }
        bool            Transactional { get; set; }
        SetupDelegate   Setup         { get; set; }

        MessageId       CurrentId { get; }

        void Init();
        void Init(long msgIdPrefix);

        IMessage         CreateMessage();
        IMessage         CreateReply(IMessage m);
        void             Send(IMessage m);
        IReceivedMessage Receive();
        IReceivedMessage ReceiveNoWait();
        void             Ack(IReceivedMessage m);
    }

}
