namespace RabbitMQ.Client.MessagePatterns.Unicast {

    using Address   = System.String;
    using MessageId = System.String;
    using Name      = System.String;

    using BasicDeliverEventArgs = RabbitMQ.Client.Events.BasicDeliverEventArgs;

    public class Message : IMessage {

        protected IBasicProperties m_properties;
        protected byte[]           m_body;
        protected string           m_routingKey;

        public IBasicProperties Properties {
            get { return m_properties; }
            set { m_properties = value; }
        }
        public byte[] Body {
            get { return m_body; }
            set { m_body = value; }
        }
        public string RoutingKey {
            get { return m_routingKey; }
            set { m_routingKey = value; }
        }

        public Address From {
            get { return Properties.UserId; }
            set { Properties.UserId = value; }
        }
        public Address To {
            get { return RoutingKey; }
            set { RoutingKey = value; }
        }
        public Address ReplyTo {
            get { return Properties.ReplyTo; }
            set { Properties.ReplyTo = value; }
        }
        public MessageId MessageId {
            get { return Properties.MessageId; }
            set { Properties.MessageId = value; }
        }
        public MessageId CorrelationId {
            get { return Properties.CorrelationId; }
            set { Properties.CorrelationId = value; }
        }

        public Message() {
        }

        public Message(IBasicProperties props, byte[] body, string rk) {
            m_properties = props;
            m_body       = body;
            m_routingKey = rk;
        }

        public IMessage CreateReply() {
            //FIXME: this should clone Properties, once we have made
            //IBasicProperties an ICloneable - see bug 21271
            IMessage m = new Message(Properties, Body, RoutingKey);

            Address origFrom = From; //TODO: drop once we clone
            m.From = To;
            m.To = ReplyTo == null ? origFrom : ReplyTo;
            m.Properties.ClearReplyTo();
            m.CorrelationId = MessageId;
            m.Properties.ClearMessageId();

            return m;
        }

    }

    public class ReceivedMessage : Message, IReceivedMessage {

        protected BasicDeliverEventArgs m_delivery;

        public BasicDeliverEventArgs Delivery {
            get { return m_delivery; }
        }

        public ReceivedMessage(BasicDeliverEventArgs delivery) :
            base(delivery.BasicProperties,
                 delivery.Body,
                 delivery.RoutingKey) {
            m_delivery = delivery;
        }

    }

    public class Messaging : IMessaging {

        protected Address m_identity;
        protected Name    m_exchangeName  = "";
        protected Name    m_queueName     = "";
        protected ushort  m_prefetchLimit = 0;

        protected SetupDelegate m_setup =
            new SetupDelegate(DefaultSetup);

        protected IConnection m_connection;
        protected IModel      m_sendingChannel;
        protected IModel      m_receivingChannel;

        protected QueueingBasicConsumer m_consumer;
        protected string                m_consumerTag;

        protected long m_msgIdPrefix;
        protected long m_msgIdSuffix;

        public event MessageEventHandler Sent;

        public Address Identity {
            get { return m_identity; }
            set { m_identity = value; }
        }
        public Name ExchangeName {
            get { return m_exchangeName; }
            set { m_exchangeName = value; }
        }

        public Name QueueName {
            get { return ("".Equals(m_queueName) ? Identity : m_queueName); }
            set { m_queueName = value; }
        }
        public ushort PrefetchLimit {
            get { return m_prefetchLimit; }
            set { m_prefetchLimit = value; }
        }

        public SetupDelegate Setup {
            get { return m_setup; }
            set { m_setup = value; }
        }

        public IConnection Connection {
            get { return m_connection; }
        }
        public IModel SendingChannel {
            get { return m_sendingChannel; }
        }
        public IModel ReceivingChannel {
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

            CreateAndConfigureChannels();
            Setup(this);
            Consume();
        }

        protected void CreateAndConfigureChannels() {
            m_sendingChannel   = m_connection.CreateModel();
            m_receivingChannel = m_connection.CreateModel();
            m_receivingChannel.BasicQos(0, PrefetchLimit, false);
        }

        protected void Consume() {
            m_consumer = new QueueingBasicConsumer(ReceivingChannel);
            m_consumerTag = ReceivingChannel.BasicConsume
                (QueueName, false, null, m_consumer);
        }

        protected void Cancel() {
            ReceivingChannel.BasicCancel(m_consumerTag);
        }

        public static void DefaultSetup(IMessaging m) {
        }

        public MessageId NextId() {
            return System.String.Format("{0:x8}{1:x8}",
                                        m_msgIdPrefix, m_msgIdSuffix++);
        }

        public void Send(IMessage m) {
            SendingChannel.BasicPublish(ExchangeName, m.RoutingKey,
                                        m.Properties, m.Body);
            //TODO: if/when SendingChannel supports 'sent'
            //notifications then we will translate those, rather than
            //firing our own here
            if (Sent != null) Sent(this, m);
        }

        public IReceivedMessage Receive() {
            try {
                return new ReceivedMessage
                    ((BasicDeliverEventArgs)m_consumer.Queue.Dequeue());
            } catch (System.IO.EndOfStreamException) {
                return null;
            }
        }

        public void Ack(IReceivedMessage m) {
            ReceivingChannel.BasicAck
                (((ReceivedMessage)m).Delivery.DeliveryTag, false);
        }

        public void Close() {
            //FIXME: only do this if we are fully initialised
            Cancel();
            SendingChannel.Abort();
            ReceivingChannel.Abort();
        }

        void System.IDisposable.Dispose() {
            Close();
        }

    }

}
