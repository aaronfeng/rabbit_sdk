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

    public delegate void SetupDelegate(IMessaging m, IModel send, IModel recv);

    public interface IMessaging : System.IDisposable {

        event MessageEventHandler Sent;

        Address Identity      { get; set; }
        Name    ExchangeName  { get; set; }
        Name    QueueName     { get; set; }

        SetupDelegate Setup { get; set; }

        ConnectionFactory ConnectionFactory { get; }
        AmqpTcpEndpoint[] Servers { get; }

        MessageId         CurrentId { get; }

        void Init(ConnectionFactory factory, params AmqpTcpEndpoint[] servers);
        void Init(long msgIdPrefix,
                  ConnectionFactory factory, params AmqpTcpEndpoint[] servers);
        void Close();

        IMessage         CreateMessage();
        IMessage         CreateReply(IMessage m);
        void             Send(IMessage m);
        IReceivedMessage Receive();
        IReceivedMessage ReceiveNoWait();
        void             Ack(IReceivedMessage m);
    }

}
