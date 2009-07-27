namespace RabbitMQ.Client.MessagePatterns.Unicast {

    using Address   = System.String;
    using MessageId = System.String;
    using Name      = System.String;

    using RabbitMQ.Client;

    public delegate void InitEventHandler(object sender, object dummy);
    public delegate void MessageEventHandler(object sender, IMessage m);

    public interface IMessage {
        IBasicProperties Properties { get; set; }
        byte[] Body { get; set; }
    }

    public interface IMessaging {

        event InitEventHandler    Initialised;
        event MessageEventHandler Sent;
        event MessageEventHandler Received;

        Address Identity      { get; set; }
        Name    ExchangeName  { get; set; }
        Name    QueueName     { get; set; }
        int     PrefetchLimit { get; set; }

        IConnection Connection       { get; }
        IModel      SendingChannel   { get; }
        IModel      ReceivingChannel { get; }

        void Init(IConnection conn);
        void Init(IConnection conn, long msgIdPrefix);
        MessageId NextId();
        void Send(IMessage m);
    }

    public class Messaging : IMessaging {

        protected Address m_identity;
        protected Name    m_exchangeName  = "";
        protected Name    m_queueName     = "";
        protected int     m_prefetchLimit = 0;

        protected IConnection m_connection;
        protected IModel      m_sendingChannel;
        protected IModel      m_receivingChannel;

        protected long m_msgIdPrefix;
        protected long m_msgIdSuffix;

        public event InitEventHandler    Initialised;
        public event MessageEventHandler Sent;
        public event MessageEventHandler Received;

        public Address Identity      {
            get { return m_identity; }
            set { m_identity = value; }
        }
        public Name    ExchangeName  {
            get { return m_exchangeName; }
            set { m_exchangeName = value; }
        }
        public Name    QueueName     {
            get { return m_queueName; }
            set { m_queueName = value; }
        }
        public int     PrefetchLimit {
            get { return m_prefetchLimit; }
            set { m_prefetchLimit = value; }
        }

        public IConnection Connection       {
            get { return m_connection; }
        }
        public IModel      SendingChannel   {
            get { return m_sendingChannel; }
        }
        public IModel      ReceivingChannel {
            get { return m_receivingChannel; }
        }

        public Messaging() {}

        public void Init(IConnection conn) {
            Init(conn, System.DateTime.UtcNow.Ticks);
        }

        public void Init(IConnection conn, long msgIdPrefix) {
            m_connection = conn;
            m_msgIdPrefix = msgIdPrefix;
            m_msgIdSuffix = 0;
            // TODO
            // 1) create channels
            // 2) raise init event, if there is a handler, otherwise
            // perform queue creation & binding
            // 3) create subscription
        }

        public MessageId NextId() {
            m_msgIdSuffix++;
            return ""; //TODO: padded msgIdPrefix ++ padded msgIdSuffix
        }

        public void Send(IMessage m) {
            //TODO: implement
        }

    }

}
