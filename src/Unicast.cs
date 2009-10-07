namespace RabbitMQ.Client.MessagePatterns.Unicast {

    using Address   = System.String;
    using MessageId = System.String;
    using Name      = System.String;

    public delegate void MessageEventHandler(IMessage m);

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
        int Pause    { get; set; }
        int Attempts { get; set; }

        void Connect(ConnectionDelegate d);
        bool Try(Thunk t, ConnectionDelegate d);
        void Close();
    }

    public delegate void SetupDelegate(IModel channel);

    public interface IMessagingCommon {
        IConnector    Connector { get; set; }
        Address       Identity  { get; set; }
    }

    public interface ISender : IMessagingCommon {
        SetupDelegate Setup         { get; set; }

        Name          ExchangeName  { get; set; }
        bool          Transactional { get; set; }

        MessageId     CurrentId { get; }

        event MessageEventHandler Sent;

        void Init();
        void Init(long msgIdPrefix);

        IMessage CreateMessage();
        IMessage CreateReply(IMessage m);
        void     Send(IMessage m);
    }

    public interface IReceiver : IMessagingCommon {
        SetupDelegate Setup     { get; set; }

        Name          QueueName { get; set; }

        void Init();

        IReceivedMessage Receive();
        IReceivedMessage ReceiveNoWait();
        void             Ack(IReceivedMessage m);
    }

    public interface IMessaging : ISender, IReceiver {
        new SetupDelegate Setup { get; set; }
        
        new void Init();
        new void Init(long msgIdPrefix);
    }

    public class Factory {

        public static IConnector CreateConnector
            (ConnectionFactory factory, params AmqpTcpEndpoint[] servers) {
            return new Connector(factory, servers);
        }

        public static ISender CreateSender() {
            return new Sender();
        }

        public static IReceiver CreateReceiver() {
            return new Receiver();;
        }

        public static IMessaging CreateMessaging() {
            return new Messaging();
        }
    }

}
