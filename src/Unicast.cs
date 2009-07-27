namespace RabbitMQ.Client.MessagePatterns.Unicast {

    using Address   = System.String;
    using MessageId = System.String;
    using Name      = System.String;

    using RabbitMQ.Client;

    public delegate void InitEventHandler(object sender, object dummy);
    public delegate void MessageEventHandler(object sender, IMessage m);

    public interface IMessage {
        Address   From          { get; set; }
        Address   To            { get; set; }
        Address   ReplyTo       { get; set; }
        MessageId MessageId     { get; set; }
        MessageId CorrelationId { get; set; }
        byte[]    MessageBody   { get; set; }
    }

    public interface IMessaging {

        event InitEventHandler    Initialised;
        event MessageEventHandler Sent;
        event MessageEventHandler Received;

        Address Identity      { get; set; }
        Name    ExchangeName  { get; set; }
        Name    QueueName     { get; set; }
        int     PrefetchLimit { get; set; }

        IBasicProperties MessageProperties { get; set; }

        IConnection Connection       { get; }
        IModel      SendingChannel   { get; }
        IModel      ReceivingChannel { get; }

        MessageId NextId();
        void Send(IMessage m);
    }

}
