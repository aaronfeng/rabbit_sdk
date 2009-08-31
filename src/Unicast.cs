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
    }

    public delegate void SetupDelegate(IMessaging m, IModel send, IModel recv);

    public interface IMessaging : System.IDisposable {

        event MessageEventHandler Sent;

        Address Identity      { get; set; }
        Name    ExchangeName  { get; set; }
        Name    QueueName     { get; set; }

        SetupDelegate Setup { get; set; }

        IConnection Connection { get; }

        void Init(IConnection conn);
        void Init(IConnection conn, long msgIdPrefix);

        IMessage         CreateMessage();
        IMessage         CreateReply(IMessage m);
        void             Send(IMessage m);
        IReceivedMessage Receive();
        void             Ack(IReceivedMessage m);
    }

}
