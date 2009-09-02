using System;

namespace RabbitMQ.Client.MessagePatterns.Unicast {

    using Address   = String;
    using MessageId = String;
    using Name      = String;

    using BasicDeliverEventArgs  = RabbitMQ.Client.Events.BasicDeliverEventArgs;
    using ClientExceptions       = RabbitMQ.Client.Exceptions;
    //TODO: find a protocol version agnostic way of doing this
    using ProtocolConstants      = RabbitMQ.Client.Framing.v0_8.Constants;

    class Message : IMessage {

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
            IMessage m = new Message(Properties.Clone() as IBasicProperties,
                                     Body,
                                     RoutingKey);
            m.From = To;
            m.To = ReplyTo == null ? From : ReplyTo;
            m.Properties.ClearReplyTo();
            m.CorrelationId = MessageId;
            m.Properties.ClearMessageId();

            return m;
        }

    }

    class ReceivedMessage : Message, IReceivedMessage {

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

    public delegate void Thunk();

    public class Messaging : IMessaging {

        protected Address m_identity;
        protected Name    m_exchangeName  = "";
        protected Name    m_queueName     = "";

        protected SetupDelegate m_setup =
            new SetupDelegate(DefaultSetup);

        protected ConnectionFactory m_factory;
        protected AmqpTcpEndpoint[] m_servers;

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

        public SetupDelegate Setup {
            get { return m_setup; }
            set { m_setup = value; }
        }

        public ConnectionFactory ConnectionFactory {
            get { return m_factory; }
        }

        public AmqpTcpEndpoint[] Servers {
            get { return m_servers; }
        }

        public MessageId CurrentId {
            get { return String.Format("{0:x8}{1:x8}",
                                       m_msgIdPrefix, m_msgIdSuffix); }
        }

        public Messaging() {}

        public void Init(ConnectionFactory factory,
                         params AmqpTcpEndpoint[] servers) {
            Init(DateTime.UtcNow.Ticks, factory, servers);
        }

        public void Init(long msgIdPrefix,
                         ConnectionFactory factory,
                         params AmqpTcpEndpoint[] servers) {
            m_msgIdPrefix = msgIdPrefix;
            m_msgIdSuffix = 0;
            m_factory     = factory;
            m_servers     = servers;

            InitialConnect();
        }

        protected Exception AttemptOperation(Thunk t) {
            try {
                t();
                return null;
            } catch (ClientExceptions.AlreadyClosedException e) {
                ShutdownEventArgs s = e.ShutdownReason;
                if (s != null &&
                    ((s.ReplyCode == ProtocolConstants.ConnectionForced) ||
                     (s.ReplyCode == ProtocolConstants.InternalError))) {
                    return e;
                } else {
                        throw e;
                }
            } catch (ClientExceptions.BrokerUnreachableException e) {
                //TODO: we may want to be more specific here
                return e;
            } catch (System.IO.IOException e) {
                //TODO: we may want to be more specific here
                return e;
            }
        }

        protected void InitialConnect() {
            Exception e = AttemptOperation(Connect);
            if (e == null) return;
            if (!Reconnect()) throw e;
        }

        protected void Connect() {
            CreateConnection();
            CreateChannels();
            Setup(this, m_sendingChannel, m_receivingChannel);
            Consume();
        }

        protected bool Reconnect() {
            //TODO: proper reconnection policy
            try {
                for (int i = 0; i < 60; i++) {
                    if (AttemptOperation(Connect) == null) return true;
                    System.Threading.Thread.Sleep(1000);
                }
            } catch (Exception) {}
            return false;
        }

        protected void CreateConnection() {
            m_connection = m_factory.CreateConnection(m_servers);
        }

        protected void CreateChannels() {
            m_sendingChannel   = m_connection.CreateModel();
            m_receivingChannel = m_connection.CreateModel();
        }

        protected void Consume() {
            m_consumer = new QueueingBasicConsumer(m_receivingChannel);
            m_consumerTag = m_receivingChannel.BasicConsume
                (QueueName, false, null, m_consumer);
        }

        protected void Cancel() {
            m_receivingChannel.BasicCancel(m_consumerTag);
        }

        protected MessageId NextId() {
            MessageId res = CurrentId;
            m_msgIdSuffix++;
            return res;
        }

        public static void DefaultSetup(IMessaging m,
                                        IModel send, IModel recv) {
        }

        public IMessage CreateMessage() {
            IMessage m = new Message();
            m.Properties = m_sendingChannel.CreateBasicProperties();
            m.From       = Identity;
            m.MessageId  = NextId();
            return m;
        }

        public IMessage CreateReply(IMessage m) {
            IMessage r  = m.CreateReply();
            r.MessageId = NextId();
            return r;
        }

        public void Send(IMessage m) {
            while(true) {
                Exception e = AttemptOperation(delegate () {
                        m_sendingChannel.BasicPublish(ExchangeName,
                                                      m.RoutingKey,
                                                      m.Properties, m.Body);
                    });
                if (e == null) break;
                if (!Reconnect()) throw e;
            }
            //TODO: if/when IModel supports 'sent' notifications then
            //we will translate those, rather than firing our own here
            if (Sent != null) Sent(this, m);
        }

        public IReceivedMessage Receive() {
            while(true) {
                try {
                    BasicDeliverEventArgs ev =
                        (BasicDeliverEventArgs)m_consumer.Queue.Dequeue();
                    return new ReceivedMessage(ev);
                } catch (System.IO.EndOfStreamException e) {
                    if (!Reconnect()) throw e;
                }
            }
        }

        public IReceivedMessage ReceiveNoWait() {
            while (true) {
                try {
                    BasicDeliverEventArgs ev =
                        (BasicDeliverEventArgs)
                        m_consumer.Queue.DequeueNoWait(null);
                    return (ev == null) ? null : new ReceivedMessage(ev);
                } catch (System.IO.EndOfStreamException e) {
                    if (!Reconnect()) throw e;
                }
            }
        }

        public void Ack(IReceivedMessage m) {
            //TODO: drop acks for messages received on a different
            //channel
            Exception e = AttemptOperation(delegate () {
                    m_receivingChannel.BasicAck
                    (((ReceivedMessage)m).Delivery.DeliveryTag, false);
                });
            //Acks must not be retried since they are tied to the
            //channel on which the message was delivered
            if (e == null) return;
            if (!Reconnect()) throw e;
        }

        public void Close() {
            if (m_connection != null) m_connection.Abort();
        }

        void IDisposable.Dispose() {
            Close();
        }

    }

}
