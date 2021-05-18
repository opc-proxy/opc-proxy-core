using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using Newtonsoft.Json.Linq;
using System.Linq ;
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
        SessionReconnectHandler reconnectHandler = null;
        static bool autoAccept = false;
        static ExitCode exitCode;
        public event EventHandler<MonItemNotificationArgs> MonitoredItemChanged;
        public NodesSelector node_selector;
        public static Logger logger = null;
        private int DefunctRequestCount = 0;
        public OPCclient(JObject config) 
        {
            user_config = config.ToObject<opcConfig>();
            var sel_config = config.ToObject<nodesConfigWrapper>();
            node_selector = new NodesSelector(sel_config.nodesLoader);
            logger = LogManager.GetLogger(this.GetType().Name);
        }

        public bool isConnected(){
            
            return (DefunctRequestCount == 0 && session.KeepAliveStopped == false);
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
            // Actually maybe not... this is debatable

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
                config.ApplicationUri = X509Utils.GetApplicationUriFromCertificate(config.SecurityConfiguration.ApplicationCertificate.Certificate);
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
            DataValueCollection data_types = ReadNodesValues(TimestampsToReturn.Neither, nodes_to_read);
            DataValueCollection ranks = ReadNodesValues(TimestampsToReturn.Neither, ranks_to_read);

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
       
        public DataValueCollection ReadNodesValues( TimestampsToReturn timestamp, ReadValueIdCollection nodes_to_read ){

            DataValueCollection data_types = new DataValueCollection();

            // limiting the number of nodes read to 500 ---> about 2kBytes + header + diagnosticInfo
            // In OPC spec I could not find any specific limit, but given the existence of continuation point
            // in Browse I prefer to introduce this here as a protection for small devices.
            for(int k=0; k <= (int)(nodes_to_read.Count / 500); k++){
                int idx = k * 500;
                int count = ( (k * 500 + 500) < nodes_to_read.Count ) ?  500 : (nodes_to_read.Count - k * 500 ) ;

                if(count == 0 ) continue;
                ReadValueIdCollection temp_nodes_to_read = new ReadValueIdCollection();
                temp_nodes_to_read.AddRange(nodes_to_read.GetRange(idx, count));
                DataValueCollection tmp_data_types;
                DiagnosticInfoCollection tmp_di;
                // only in case there is a connection open, otherwise it hungs up forever, see issue: https://github.com/OPCFoundation/UA-.NETStandard/issues/1091
                if(isConnected()) session.Read(null,0.0, TimestampsToReturn.Server, nodes_to_read, out tmp_data_types, out tmp_di);
                else {
                    tmp_data_types = new DataValueCollection();
                    foreach (var node in temp_nodes_to_read)
                    {
                        tmp_data_types.Add( new DataValue(){
                            StatusCode = StatusCodes.BadNoCommunication,
                            Value = null,
                            ServerTimestamp = DateTime.UtcNow,
                            SourceTimestamp = DateTime.UtcNow
                        } );
                    }
                }
                data_types.AddRange(tmp_data_types);
            }
            return data_types;
        }

        public  Task<List<ReadVarResponse>> ReadNodesValuesWrapper( List<serverNode> nodes){
            return Task.Run(()=>{
                ReadValueIdCollection r = new ReadValueIdCollection();
                List<ReadVarResponse> resp = new List<ReadVarResponse>();
                for( int k=0; k < nodes.Count; k++){
                    r.Add(new ReadValueId{ 
                        NodeId = new NodeId(nodes[k].serverIdentifier),
                        AttributeId = Attributes.Value
                    });
                }
                var dvc = ReadNodesValues(TimestampsToReturn.Server, r);

                for( int k=0; k < nodes.Count; k++){
                    var temp_resp = new ReadVarResponse(nodes[k].name, dvc[k].StatusCode.Code);
                    temp_resp.timestamp = dvc[k].ServerTimestamp;
                    if(dvc[k].Value != null) temp_resp.value = dvc[k].Value;
                    temp_resp.systemType = nodes[k].systemType;
                    resp.Add( temp_resp );
                    logger.Debug("Read node " + nodes[k].name + ". Returns: " );
                    logger.Debug("\t Node ID: "       + nodes[k].serverIdentifier.ToString());
                    if(dvc[k].Value != null) logger.Debug("\t Value: " + dvc[k].Value.ToString());
                    logger.Debug("\t TimeStamp: " + dvc[k].ServerTimestamp.ToString());
                    logger.Debug("\t Status Code: " + dvc[k].StatusCode.ToString()); 

                    // Send notification about the new read values
                    MonItemNotificationArgs notification = new MonItemNotificationArgs();
                    var d = new DataValue(){ 
                        StatusCode = dvc[k].StatusCode,
                        Value = dvc[k].Value,
                        ServerTimestamp =  dvc[k].ServerTimestamp,
                        SourceTimestamp = dvc[k].ServerTimestamp,
                    };
                    notification.values = new List<DataValue>(){d};
                    notification.name = nodes[k].name;
                    notification.dataType = Type.GetType(nodes[k].systemType);
                    _sendNotification(notification);
                }
                return resp;
            });
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
                // if session is not connected to server
                if(!isConnected()) {
                    response[i].success = false ;
                    response[i].statusCode = StatusCodes.BadNoCommunication;
                }
            }
            // don't do the write, somehow it hungs up see Issue: https://github.com/OPCFoundation/UA-.NETStandard/issues/1091
            if(!isConnected()) return response;

            var sCodes = await Task.Factory.FromAsync<StatusCodeCollection>(beginWriteWrapper,endWriteWrapper, valuesToWrite);
            // By OPC Specs is assumed "sCodes" is a List of results for the Nodes to write (see 7.34 for StatusCode definition). 
            // The size and order of the list matches the size and order of the nodesToWrite request parameter. 
            // There is one entry in this list for each Node contained in the nodesToWrite parameter.
            // Note: write does not need to send an ad hoc notification as the read, since if the write procedure is successfull
            // the opc-server must send and update-monitored-item call.
            for(int k=0; k< sCodes.Count; k++){
                
                response[k].success = StatusCode.IsGood(sCodes[k]) ;
                response[k].statusCode = sCodes[k];
                if(StatusCode.IsBad(sCodes[k]))
                {            
                    logger.Error("Failed write request on OPC-server for node: "+response[k].name +" status code " + sCodes[k].ToString());
                }
                else{
                    _serviceManager.db.updateBuffer(response[k].name,response[k].value, DateTime.UtcNow, sCodes[k].Code);
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
                DefunctRequestCount += 1;
                logger.Error("Connection lost... curent status: {0} - retry {1} ", e.Status, DefunctRequestCount);
                // set all nodes to Unreachable, the read will fail for all nodes, notify all handlers, update db cache
                // just do this on  first trial
                if( DefunctRequestCount == 1) {
                    var nodes = _serviceManager.db.getDbNodes();
                    notifyErrorOnNodes(nodes,StatusCodes.BadNotConnected);
                    notifyNodesUnavailAfter(user_config.nodesUnavailAfter_sec);
                }

                if (reconnectHandler == null)
                {
                    logger.Info("Reconnecting...");
                    reconnectHandler = new SessionReconnectHandler();
                    reconnectHandler.BeginReconnect(sender, user_config.reconnectPeriod * 1000, Client_ReconnectComplete);
                }
            }            
        }

        public async void notifyErrorOnNodes(List<serverNode> nodes, StatusCode err)
        {
            foreach (var node in nodes)
            {
                // get var
                var db_var = await _serviceManager.db.readValue(node.name);
                // Send notification about the new read values
                MonItemNotificationArgs notification = new MonItemNotificationArgs();
                var d = new DataValue(err);
                d.Value = db_var.value;
                d.ServerTimestamp = db_var.timestamp;
                d.SourceTimestamp = db_var.timestamp;
                notification.values = new List<DataValue>();
                notification.values.Add(d);
                notification.name = node.name;

                notification.dataType = Type.GetType(node.systemType);
                _sendNotification(notification);    
            }  

        }

        private void notifyNodesUnavailAfter(int wait_time_sec) {
            
            var cancel = _serviceManager.cancellationToken.Token;
            
            Task t = Task.Run(async ()=>{
                await Task.Delay(wait_time_sec * 1000, cancel);
                if(!cancel.IsCancellationRequested && !isConnected())
                {
                    var nodes = _serviceManager.db.getDbNodes();
                    notifyErrorOnNodes( nodes, StatusCodes.BadDataUnavailable );
                }
            },
            cancel);

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

            logger.Info("Session Restored.");
            DefunctRequestCount = 0;
            // update all nodes
            var nodes = _serviceManager.db.getDbNodes();
            ReadNodesValuesWrapper(nodes);
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
            _sendNotification(notification);
        }

        /// <summary>
        /// This is an internal function to notify for a change of a variable,
        /// is not supposed to be used by user, but in case is needed is available.
        /// </summary>
        /// <param name="notification"></param>
        public void _sendNotification(MonItemNotificationArgs notification){
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

        /// <summary>
        /// Nodes values are not only subscribed to change but also read periodically every nodeReadPeriod
        /// to make sure their values are always fresh and in good state. Value is in seconds.
        /// </summary>
        public int nodeReadPeriod_sec{get; set;}

        /// <summary>
        /// If connection is lost, nodes are immediately set to BadConnection error, however this happens often,
        /// for example pushing new software on server. After this time, nodes are set to unavailable
        /// </summary>
        public int nodesUnavailAfter_sec {get; set;}

        public opcConfig(){
            opcServerURL = "none";
            reconnectPeriod = 10;     
            publishingInterval = 1000;
            opcSystemName = "OPC";
            nodeReadPeriod_sec = 60 * 5;
            nodesUnavailAfter_sec = 60*1;
        }
    }

}

