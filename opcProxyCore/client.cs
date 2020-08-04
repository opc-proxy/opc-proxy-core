using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using Newtonsoft.Json.Linq;

using OpcProxyCore;
using converter;

namespace OpcProxyClient
{

    public enum ExitCode : int
    {
        Ok = 0,
        ErrorCreateApplication = 0x11,
        ErrorDiscoverEndpoints = 0x12,
        ErrorCreateSession = 0x13,
        ErrorBrowseNamespace = 0x14,
        ErrorCreateSubscription = 0x15,
        ErrorMonitoredItem = 0x16,
        ErrorAddSubscription = 0x17,
        ErrorRunning = 0x18,
        ErrorNoKeepAlive = 0x30,
        ErrorInvalidCommandLine = 0x100
    };

    public class MonItemNotificationArgs : EventArgs{
        public IList<DataValue> values {get; set;}
        public string name {get; set;}
        public Type dataType {get; set;}
    }

    public class OPCclient : Managed
    {
        public opcConfig user_config;
        public Session session;
        SessionReconnectHandler reconnectHandler;
        static bool autoAccept = false;
        static ExitCode exitCode;

        public event EventHandler<MonItemNotificationArgs> MonitoredItemChanged;
        public NodesSelector node_selector;
        public static Logger logger = null;
        public OPCclient(JObject config) 
        {
            user_config = config.ToObject<opcConfig>();
            var sel_config = config.ToObject<nodesConfigWrapper>();
            node_selector = new NodesSelector(sel_config.nodesLoader);
            logger = LogManager.GetLogger(this.GetType().Name);
        }


        public void connect()
        {
            try
            {
                ConsoleSampleClient().Wait();
            }
            catch (Exception ex)
            {
                // Utils.Trace("ServiceResultException:" + ex.Message);
                logger.Error("Failed to connect to server at URL: " + user_config.opcServerURL);
                logger.Error("Exception: " + ex.Message);
                System.Environment.Exit(0); 
                return;
            }

            // FIXME - Retry strategy to be implemented here

        }

        public static ExitCode ExitCode { get => exitCode; }

        private async Task ConsoleSampleClient()
        {
            logger.Info("Creating Application Configuration.");
            exitCode = ExitCode.ErrorCreateApplication;

            ApplicationInstance application = new ApplicationInstance
            {
                ApplicationName = "UA Core Sample Client",
                ApplicationType = ApplicationType.Client,
                ConfigSectionName = Utils.IsRunningOnMono() ? "Opc.Ua.MonoSampleClient" : "Opc.Ua.SampleClient"
            };

            // load the application configuration.
            ApplicationConfiguration config = await application.LoadApplicationConfiguration(false);

            if(config == null) {
                logger.Error("Probably missing file 'Opc.Ua.SampleClient.Config.xml'");
                throw new Exception("Application configuration is empty");
            }

            // check the application certificate.
            bool haveAppCertificate = await application.CheckApplicationInstanceCertificate(false, 0);
            if (!haveAppCertificate)
            {
                throw new Exception("Application instance certificate invalid!");
            }

            if (haveAppCertificate)
            {
                config.ApplicationUri = Utils.GetApplicationUriFromCertificate(config.SecurityConfiguration.ApplicationCertificate.Certificate);
                if (config.SecurityConfiguration.AutoAcceptUntrustedCertificates)
                {
                    autoAccept = true;
                    logger.Warn("Automatically accepting untrusted certificates. Do not use in production. Change in 'OPC.Ua.SampleClient.Config.xml'.");
                }
                config.CertificateValidator.CertificateValidation += new CertificateValidationEventHandler(CertificateValidator_CertificateValidation);
            }
            else
            {
                logger.Warn("missing application certificate, using unsecure connection.");
            }

            logger.Info("Trying to connect to server endpoint:  {0}", user_config.opcServerURL);
            exitCode = ExitCode.ErrorDiscoverEndpoints;
            var selectedEndpoint = CoreClientUtils.SelectEndpoint(user_config.opcServerURL, haveAppCertificate, 15000);
            logger.Info("Selected endpoint uses the following security policy: {0}",
                selectedEndpoint.SecurityPolicyUri.Substring(selectedEndpoint.SecurityPolicyUri.LastIndexOf('#') + 1));

            logger.Info("Creating a session with OPC UA server.");
            exitCode = ExitCode.ErrorCreateSession;
            var endpointConfiguration = EndpointConfiguration.Create(config);
            var endpoint = new ConfiguredEndpoint(null, selectedEndpoint, endpointConfiguration);
            session = await Session.Create(config, endpoint, false, "OPC UA Console Client", 60000, new UserIdentity(new AnonymousIdentityToken()), null);
            
            // register keep alive handler
            session.KeepAlive += Client_KeepAlive;
        }

        public List<dbNode> surf(){
            logger.Info("Surfing recursively trough server tree....");
            exitCode = ExitCode.ErrorBrowseNamespace;
            ReferenceDescriptionCollection references;
            Byte[] continuationPoint;
            Queue<NodeId> node_list = new Queue<NodeId>();
            
            node_list.Enqueue(ObjectIds.ObjectsFolder);
            
            logger.Debug(" DisplayName, BrowseName, NodeClass");

            ReadValueIdCollection nodes_to_read = new ReadValueIdCollection();
            ReadValueIdCollection ranks_to_read = new ReadValueIdCollection();
            List<String> node_names = new List<string>();

            while(node_list.Count > 0){

                var temp_node = node_list.Dequeue();

                session.Browse(
                    null,
                    null,
                    temp_node,
                    0u,
                    BrowseDirection.Forward,
                    ReferenceTypeIds.HierarchicalReferences,
                    true,
                    (uint)NodeClass.Variable | (uint)NodeClass.Object ,
                    out continuationPoint,
                    out references
                );
                
                foreach (var rd in references) {
                    node_list.Enqueue(ExpandedNodeId.ToNodeId(rd.NodeId, session.NamespaceUris));
                    // Adding the node to list if pass user selections
                    if(rd.NodeClass == NodeClass.Variable && node_selector.selectNode(rd)){
                        nodes_to_read.Add(new ReadValueId{ 
                            NodeId = ExpandedNodeId.ToNodeId(rd.NodeId, session.NamespaceUris), 
                            AttributeId = Attributes.DataType
                        });
                        ranks_to_read.Add(new ReadValueId{ 
                            NodeId = ExpandedNodeId.ToNodeId(rd.NodeId, session.NamespaceUris), 
                            AttributeId = Attributes.ValueRank
                        });
                        node_names.Add( node_selector.getNameFromReference(rd));
                    }
                }
                while( continuationPoint != null ) {
                    Byte[] revCP;
                    session.BrowseNext(null, false, continuationPoint,out revCP,out references);
                    foreach (var rd in references) {
                        node_list.Enqueue(ExpandedNodeId.ToNodeId(rd.NodeId, session.NamespaceUris));
                        // Adding the node to list if pass user selections
                        if(rd.NodeClass == NodeClass.Variable && node_selector.selectNode(rd)){
                            nodes_to_read.Add(new ReadValueId{ 
                                NodeId = ExpandedNodeId.ToNodeId(rd.NodeId, session.NamespaceUris), 
                                AttributeId = Attributes.DataType
                            });
                            ranks_to_read.Add(new ReadValueId{ 
                                NodeId = ExpandedNodeId.ToNodeId(rd.NodeId, session.NamespaceUris), 
                                AttributeId = Attributes.ValueRank
                            });
                            node_names.Add( node_selector.getNameFromReference(rd));
                        }
                    }
                    continuationPoint = revCP;
                }
            }

            logger.Debug("Retriving data types of the selected nodes...");
            DataValueCollection data_types;
            DataValueCollection ranks;
            DiagnosticInfoCollection di;
            session.Read(null,0.0, TimestampsToReturn.Neither, nodes_to_read,out data_types, out di);
            session.Read(null,0.0, TimestampsToReturn.Neither, ranks_to_read,out ranks, out di);
            
            List<dbNode> return_nodes = new List<dbNode>();
            for(int i=0; i< nodes_to_read.Count; i++){
                
                dbNode temp_dbNode = new dbNode();
                if(nodes_to_read[i].NodeId.IdType == IdType.String)
                    temp_dbNode.identifier = "s=" + nodes_to_read[i].NodeId.Identifier.ToString();
                else if(nodes_to_read[i].NodeId.IdType == IdType.Numeric){
                    temp_dbNode.identifier = "i=" + nodes_to_read[i].NodeId.Identifier.ToString();
                }
                else continue;

                temp_dbNode.name = node_names[i];
                // namespace index is equal to namespace uri table indexes
                temp_dbNode.internalIndex  = nodes_to_read[i].NodeId.NamespaceIndex;
                BuiltInType b = TypeInfo.GetBuiltInType( (NodeId)data_types[i].Value ) ;
                temp_dbNode.systemType = TypeInfo.GetSystemType(b, (Int32)ranks[i].Value).ToString();

                // skip if not builtIn value
                if (!temp_dbNode.systemType.StartsWith("System") || temp_dbNode.systemType.ToLower() == "null"){
                    logger.Error("type '"+temp_dbNode.systemType +"' not supported for node: '"+ temp_dbNode.name +"'");
                    continue;
                }
                if((Int32)ranks[i].Value > 1) {
                    logger.Error("Arrays are not supported. node: '"+ temp_dbNode.name +"'");
                    continue;
                }

                return_nodes.Add(temp_dbNode);
                logger.Debug( "Adding Node " + temp_dbNode.name  + "  of type " + temp_dbNode.systemType);
            }

            return return_nodes;
        }
       
        private IAsyncResult beginWriteWrapper(AsyncCallback callback, object nodes_to_write){
            return session.BeginWrite(null, (WriteValueCollection)nodes_to_write, callback, null);
        }
        private StatusCodeCollection endWriteWrapper( IAsyncResult as_result){
            DiagnosticInfoCollection diagnosticInfos = null;
            StatusCodeCollection status = null;
            session.EndWrite(as_result, out status, out diagnosticInfos);
            return status;
        }

        public Task<StatusCodeCollection> badStatusCall(){
            StatusCodeCollection badstatus = new StatusCodeCollection();
            badstatus.Add(StatusCodes.Bad);
            return Task.FromResult(badstatus);
        }
        public async Task<List<WriteVarResponse>> asyncWrite(List<serverNode> nodes, List<object> values)
        {
            List<WriteVarResponse> response = new List<WriteVarResponse>();
            WriteValueCollection valuesToWrite = new WriteValueCollection();

            for(int i=0; i< nodes.Count; i++)
            {
                WriteValue valueToWrite = new WriteValue();
                NodeId m_nodeId = new NodeId(nodes[i].serverIdentifier);
                valueToWrite.NodeId = m_nodeId;
                valueToWrite.AttributeId = Attributes.Value;
                valueToWrite.Value.Value = values[i] ;
                valueToWrite.Value.StatusCode = StatusCodes.Good;
                valuesToWrite.Add(valueToWrite);
                // register that the node has been sent, success by default.
                response.Add( new WriteVarResponse(nodes[i].name, values[i]) );
                logger.Debug("Sending write request to server for node " + nodes[i].name + ". Request INFO: " );
                logger.Debug("\t Node ID: "       + valueToWrite.NodeId.ToString());
                logger.Debug("\t Attribute ID: " + valueToWrite.AttributeId.ToString());
                logger.Debug("\t Value: " + valueToWrite.Value.Value.ToString());
                logger.Debug("\t TimeStamp: " + valueToWrite.Value.SourceTimestamp.ToString());
                logger.Debug("\t Status Code: " + valueToWrite.Value.StatusCode.ToString());
            }
            var sCodes = await Task.Factory.FromAsync<StatusCodeCollection>(beginWriteWrapper,endWriteWrapper, valuesToWrite);
            // By OPC Specs is assumed "sCodes" is a List of results for the Nodes to write (see 7.34 for StatusCode definition). 
            // The size and order of the list matches the size and order of the nodesToWrite request parameter. 
            // There is one entry in this list for each Node contained in the nodesToWrite parameter.
            for(int k=0; k< sCodes.Count; k++){
                if(StatusCode.IsBad(sCodes[k]))
                {            
                    response[k].success = false ;
                    response[k].statusCode = sCodes[k];
                    logger.Error("Failed write request on OPC-server for node: "+response[k].name +" status code " + sCodes[k].ToString());
                }
            }
            return response;
        }

        

        public void subscribe(List<serverNode> serverNodes, List<EventHandler<MonItemNotificationArgs>> handlers){


            logger.Info("Creating a subscription with publishing interval of 1 second.");
            exitCode = ExitCode.ErrorCreateSubscription;
            var subscription = new Subscription(session.DefaultSubscription) { PublishingInterval = user_config.publishingInterval };

            logger.Info("Adding a list of monitored nodes to the subscription.");
            exitCode = ExitCode.ErrorMonitoredItem;
            var list = new List<MonitoredItem> {};

            logger.Info("Number of nodes to be monitored: " + serverNodes.Count);

            // addind client notification handler
            foreach( var node in serverNodes){
                var monItem = new MonitoredItem(subscription.DefaultItem)
                {
                    DisplayName = node.name, 
                    StartNodeId = node.serverIdentifier
                };
                monItem.Notification += OnNotification;
                list.Add(monItem);
            }
            // Adding all Connectors defined notification handlers
            foreach(var handler in handlers) {
                MonitoredItemChanged += handler;
            }

            subscription.AddItems(list);

            logger.Info("Adding the subscription to the session.");
            exitCode = ExitCode.ErrorAddSubscription;
            session.AddSubscription(subscription);
            subscription.Create();

            exitCode = ExitCode.ErrorRunning;

            
        }


        private void Client_KeepAlive(Session sender, KeepAliveEventArgs e)
        {
            if (e.Status != null && ServiceResult.IsNotGood(e.Status))
            {
                Console.WriteLine("{0} {1}/{2}", e.Status, sender.OutstandingRequestCount, sender.DefunctRequestCount);

                if (reconnectHandler == null)
                {
                    Console.WriteLine("--- RECONNECTING ---");
                    reconnectHandler = new SessionReconnectHandler();
                    reconnectHandler.BeginReconnect(sender, user_config.reconnectPeriod * 1000, Client_ReconnectComplete);
                }
            }
        }

        private void Client_ReconnectComplete(object sender, EventArgs e)
        {
            // ignore callbacks from discarded objects.
            if (!Object.ReferenceEquals(sender, reconnectHandler))
            {
                return;
            }

            session = reconnectHandler.Session;
            reconnectHandler.Dispose();
            reconnectHandler = null;

            Console.WriteLine("--- RECONNECTED ---");
        }

        /// <summary>
        /// This is a wrpapper, it is needed because once you call DequeueValues on a monitored item 
        /// the cache of the monitored item is actually cleared, so is not a good object to pass in an 
        /// event handler because the second handler that runs will have an empty MonitoredItem.
        /// So here the values are unpacked from the original MonitoredItem, wrapped in a less arcane way 
        /// in another class and then an event is emitted that pass that values to all handlers.
        /// </summary>
        /// <param name="item"></param>
        /// <param name="e"></param>
        private void OnNotification(MonitoredItem item, MonitoredItemNotificationEventArgs e)
        {
            MonItemNotificationArgs notification = new MonItemNotificationArgs();
            notification.values = item.DequeueValues();
            notification.name = item.DisplayName;
            // Monitored Item does not return a data type since is general it can be used with nodes that are not variables.
            // here we hit the memory DB to get the dataType
            dbNode n = _serviceManager.db.getDbNode(item.DisplayName);
            if(n!=null){
                notification.dataType = Type.GetType(n.systemType);
            }
            EventHandler<MonItemNotificationArgs> handler = MonitoredItemChanged; 
            if (handler != null)
            {
                handler(this,notification);
            }
        }

        private static void CertificateValidator_CertificateValidation(CertificateValidator validator, CertificateValidationEventArgs e)
        {
            if (e.Error.StatusCode == StatusCodes.BadCertificateUntrusted)
            {
                e.Accept = autoAccept;
                if (autoAccept)
                {
                    Console.WriteLine("Accepted Certificate: {0}", e.Certificate.Subject);
                }
                else
                {
                    Console.WriteLine("Rejected Certificate: {0}", e.Certificate.Subject);
                }
            }
        }

    }
/// <summary>
/// class that holds Json configuration for the opc client
/// </summary>
    public class opcConfig{
        /// <summary> OPC server TCP URL endpoint</summary>
        public  string  opcServerURL { get; set; }

        /// <summary> Time interval [seconds] to wait before retry to reconnect to OPC server</summary>
        public int reconnectPeriod {get; set;}
        
        /// <summary> This is a subscription parameter, time intervall [millisecond] at which the OPC server will send node values updates.</summary>
        public int publishingInterval {get; set;}

        /// <summary>
        /// Name of the OPC system that will be used to identify 
        /// </summary>
        public string opcSystemName {get; set;}

        public opcConfig(){
            opcServerURL = "none";
            reconnectPeriod = 10;     
            publishingInterval = 1000;
            opcSystemName = "OPC";
        }
    }

}

