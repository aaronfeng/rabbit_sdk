using System;

namespace RabbitMQ.Client.MessagePatterns.Unicast {

    using Address   = String;
    using MessageId = String;
    using Name      = String;

    using EndOfStreamException   = System.IO.EndOfStreamException;

    using BasicDeliverEventArgs  = RabbitMQ.Client.Events.BasicDeliverEventArgs;
    using ClientExceptions       = RabbitMQ.Client.Exceptions;
    using SharedQueue            = RabbitMQ.Util.SharedQueue;
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

        protected IModel m_channel;
        protected BasicDeliverEventArgs m_delivery;

        public bool Redelivered {
            get { return m_delivery.Redelivered; }
        }

        public IModel Channel {
            get { return m_channel; }
        }

        public BasicDeliverEventArgs Delivery {
            get { return m_delivery; }
        }

        public ReceivedMessage(IModel channel, BasicDeliverEventArgs delivery) :
            base(delivery.BasicProperties,
                 delivery.Body,
                 delivery.RoutingKey) {
            m_channel  = channel;
            m_delivery = delivery;
        }

    }

    class QueueingMessageConsumer : DefaultBasicConsumer {

        protected SharedQueue m_queue;

        public QueueingMessageConsumer(IModel model) : base (model) {
            m_queue = new SharedQueue();
        }

        public SharedQueue Queue
        {
            get { return m_queue; }
        }

        public override void OnCancel()
        {
            m_queue.Close();
            base.OnCancel();
        }

        public override void HandleBasicDeliver(string consumerTag,
                                                ulong deliveryTag,
                                                bool redelivered,
                                                string exchange,
                                                string routingKey,
                                                IBasicProperties properties,
                                                byte[] body)
        {
            BasicDeliverEventArgs e = new BasicDeliverEventArgs();
            e.ConsumerTag = consumerTag;
            e.DeliveryTag = deliveryTag;
            e.Redelivered = redelivered;
            e.Exchange = exchange;
            e.RoutingKey = routingKey;
            e.BasicProperties = properties;
            e.Body = body;
            m_queue.Enqueue(new ReceivedMessage(Model, e));
        }

    }

    class Connector : IConnector {

        protected int m_pause    = 1000; //ms
        protected int m_attempts = 60;

        protected ConnectionFactory m_factory;
        protected AmqpTcpEndpoint[] m_servers;

        protected IConnection m_connection;

        public int Pause {
            get { return m_pause; }
            set { m_pause = value; }
        }
        public int Attempts {
            get { return m_attempts; }
            set { m_attempts = value; }
        }

        public ConnectionFactory ConnectionFactory {
            get { return m_factory; }
        }
        public AmqpTcpEndpoint[] Servers {
            get { return m_servers; }
        }

        public Connector(ConnectionFactory factory,
                         params AmqpTcpEndpoint[] servers) {
            m_factory = factory;
            m_servers = servers;
        }

        public void Connect(ConnectionDelegate d) {
            IConnection conn = Connect();
            Exception e = Try(delegate() { d(conn); });
            if (e == null) return;
            if (!Reconnect(d)) throw e;
        }

        public bool Try(Thunk t, ConnectionDelegate d) {
            Exception e = Try(t);
            if (e == null) return true;
            if (!Reconnect(d)) throw e;
            return false;
        }

        public void Close() {
            //TODO: find a way to abort a pending reconnect, rather
            //than waiting here
            lock (this) {
                if (m_connection != null) m_connection.Abort();
            }
        }

        void IDisposable.Dispose() {
            Close();
        }

        protected IConnection Connect() {
            lock (this) {
                if (m_connection != null) {
                    ShutdownEventArgs closeReason = m_connection.CloseReason;
                    if (closeReason == null) return m_connection;
                    if (!IsShutdownRecoverable(closeReason))
                        throw new ClientExceptions.AlreadyClosedException
                            (closeReason);
                }
                Exception e = null;
                for (int i = 0; i < Attempts; i++) {
                    e = Try(delegate {
                            m_connection =
                            ConnectionFactory.CreateConnection(Servers);
                        });
                    if (e == null) return m_connection;
                    System.Threading.Thread.Sleep(Pause);
                }
                throw(e);
            }
        }

        protected bool Reconnect(ConnectionDelegate d) {
            try {
                while (true) {
                    IConnection conn = Connect();
                    Exception e = Try(delegate() { d(conn); });
                    if (e == null) return true;
                }
            } catch (Exception) {}
            return false;
        }

        protected static bool IsShutdownRecoverable(ShutdownEventArgs s) {
            return (s != null && s.Initiator != ShutdownInitiator.Application &&
                    ((s.ReplyCode == ProtocolConstants.ConnectionForced) ||
                     (s.ReplyCode == ProtocolConstants.InternalError) ||
                     (s.Cause is EndOfStreamException)));
        }

        protected static Exception Try(Thunk t) {
            try {
                t();
                return null;
            } catch (ClientExceptions.AlreadyClosedException e) {
                if (IsShutdownRecoverable(e.ShutdownReason)) {
                    return e;
                } else {
                    throw e;
                }
            } catch (ClientExceptions.OperationInterruptedException e) {
                if (IsShutdownRecoverable(e.ShutdownReason)) {
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

    }

    class Sender : ISender {

        protected IConnector m_connector;
        protected SetupDelegate m_setup;
        protected Address m_identity;
        protected Name m_exchangeName  = "";
        protected bool m_transactional = true;

        protected IModel m_channel;

        protected long m_msgIdPrefix;
        protected long m_msgIdSuffix;

        public event MessageEventHandler Sent;

        public IConnector Connector {
            get { return m_connector; }
            set { m_connector = value; }
        }

        public SetupDelegate Setup {
            get { return m_setup; }
            set { m_setup = value; }
        }

        public Address Identity {
            get { return m_identity; }
            set { m_identity = value; }
        }

        public Name ExchangeName {
            get { return m_exchangeName; }
            set { m_exchangeName = value; }
        }

        public bool Transactional {
            get { return m_transactional; }
            set { m_transactional = value; }
        }

        public MessageId CurrentId {
            get { return String.Format("{0:x8}{1:x8}",
                                       m_msgIdPrefix, m_msgIdSuffix); }
        }

        public Sender() {}

        public void Init() {
            Init(DateTime.UtcNow.Ticks);
        }

        public void Init(long msgIdPrefix) {
            m_msgIdPrefix = msgIdPrefix;
            m_msgIdSuffix = 0;

            Connector.Connect(Connect);
        }

        protected void Connect(IConnection conn) {
            m_channel = conn.CreateModel();
            if (Transactional) m_channel.TxSelect();
            if (Setup != null) Setup(m_channel);
        }

        protected MessageId NextId() {
            MessageId res = CurrentId;
            m_msgIdSuffix++;
            return res;
        }

        public IMessage CreateMessage() {
            IMessage m = new Message();
            m.Properties = m_channel.CreateBasicProperties();
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
                if (Connector.Try(delegate () {
                            m_channel.BasicPublish(ExchangeName,
                                                   m.RoutingKey,
                                                   m.Properties, m.Body);
                            if (Transactional) m_channel.TxCommit();
                        }, Connect)) break;
            }
            //TODO: if/when IModel supports 'sent' notifications then
            //we will translate those, rather than firing our own here
            if (Sent != null) Sent(m);
        }

    }

    class Receiver : IReceiver {

        protected IConnector m_connector;
        protected SetupDelegate m_setup;
        protected Address m_identity;
        protected Name    m_queueName = "";

        protected IModel m_channel;

        protected QueueingMessageConsumer m_consumer;
        protected string                  m_consumerTag;

        public IConnector Connector {
            get { return m_connector; }
            set { m_connector = value; }
        }

        public SetupDelegate Setup {
            get { return m_setup; }
            set { m_setup = value; }
        }

        public Address Identity {
            get { return m_identity; }
            set { m_identity = value; }
        }

        public Name QueueName {
            get { return ("".Equals(m_queueName) ? Identity : m_queueName); }
            set { m_queueName = value; }
        }

        public Receiver() {}

        public void Init() {
            Connector.Connect(Connect);
        }

        protected void Connect(IConnection conn) {
            m_channel = conn.CreateModel();
            if (Setup != null) Setup(m_channel);
            Consume();
        }

        protected void Consume() {
            m_consumer = new QueueingMessageConsumer(m_channel);
            m_consumerTag = m_channel.BasicConsume
                (QueueName, false, null, m_consumer);
        }

        protected void Cancel() {
            m_channel.BasicCancel(m_consumerTag);
        }

        public IReceivedMessage Receive() {
            IReceivedMessage res = null;
            while(true) {
                if (Connector.Try(delegate() {
                            res = m_consumer.Queue.Dequeue()
                                as IReceivedMessage;
                        }, Connect)) break;
            }
            return res;
        }

        public IReceivedMessage ReceiveNoWait() {
            IReceivedMessage res = null;
            while (true) {
                if (Connector.Try(delegate() {
                            res = m_consumer.Queue.DequeueNoWait(null)
                                as IReceivedMessage;
                        }, Connect)) break;
            }
            return res;
        }

        public void Ack(IReceivedMessage m) {
            ReceivedMessage r = m as ReceivedMessage;
            if (r.Channel != m_channel) {
                //must have been reconnected; drop ack since there is
                //no place for it to go
                return;
            }
            //Acks must not be retried since they are tied to the
            //channel on which the message was delivered
            Connector.Try(delegate () {
                    m_channel.BasicAck(r.Delivery.DeliveryTag, false);
                }, Connect);
        }

    }

    class Messaging : IMessaging {

        protected ISender   m_sender    = new Sender();
        protected IReceiver m_receiver  = new Receiver();

        public IConnector Connector {
            get { return m_sender.Connector; }
            set { m_sender.Connector = value; m_receiver.Connector = value; }
        }

        public SetupDelegate Setup {
            get { return m_sender.Setup; }
            set { m_sender.Setup = value; m_receiver.Setup = value; }
        }

        SetupDelegate ISender.Setup {
            get { return m_sender.Setup; }
            set { m_sender.Setup = value; }
        }

        SetupDelegate IReceiver.Setup {
            get { return m_receiver.Setup; }
            set { m_receiver.Setup = value; }
        }

        public Address Identity {
            get { return m_sender.Identity; }
            set { m_sender.Identity = value; m_receiver.Identity = value; }
        }

        public Name ExchangeName {
            get { return m_sender.ExchangeName; }
            set { m_sender.ExchangeName = value; }
        }

        public bool Transactional {
            get { return m_sender.Transactional; }
            set { m_sender.Transactional = value; }
        }

        public MessageId CurrentId {
            get { return m_sender.CurrentId; }
        }

        public Name QueueName {
            get { return m_receiver.QueueName; }
            set { m_receiver.QueueName = value; }
        }

        public event MessageEventHandler Sent {
            add { m_sender.Sent += value; }
            remove { m_sender.Sent -= value; }
        }

        void ISender.Init() {
            m_sender.Init();
        }

        void ISender.Init(long msgIdPrefix) {
            m_sender.Init(msgIdPrefix);
        }

        void IReceiver.Init() {
            m_receiver.Init();
        }

        public void Init() {
            (this as ISender).Init();
            (this as IReceiver).Init();
        }

        public void Init(long msgIdPrefix) {
            (this as ISender).Init(msgIdPrefix);
            (this as IReceiver).Init();
        }

        public IMessage CreateMessage() {
            return m_sender.CreateMessage();
        }

        public IMessage CreateReply(IMessage m) {
            return m_sender.CreateReply(m);
        }

        public void Send(IMessage m) {
            m_sender.Send(m);
        }

        public IReceivedMessage Receive() {
            return m_receiver.Receive();
        }

        public IReceivedMessage ReceiveNoWait() {
            return m_receiver.ReceiveNoWait();
        }

        public void Ack(IReceivedMessage m) {
            m_receiver.Ack(m);
        }

        public Messaging() {
        }

    }

}
