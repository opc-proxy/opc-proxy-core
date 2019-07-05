using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;
using System;
using System.IO;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client.Controls;

using  converter;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using ProxyUtils;

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
    }

    public class OPCclient : Managed
    {
        const int ReconnectPeriod = 10;
        public Session session;
        SessionReconnectHandler reconnectHandler;
        string endpointURL;
        int clientRunTime = Timeout.Infinite;
        static bool autoAccept = false;
        static ExitCode exitCode;

        public event EventHandler<MonItemNotificationArgs> MonitoredItemChanged;

        public OPCclient(JObject config) 
        {
            var _config = config.ToObject<opcConfig>();
            endpointURL = _config.endpointURL;
            autoAccept = _config.autoAccept;
            clientRunTime = _config.stopTimeout <= 0 ? Timeout.Infinite : _config.stopTimeout * 1000;
        }


        public void connect()
        {
            try
            {
                ConsoleSampleClient().Wait();
            }
            catch (Exception ex)
            {
                Utils.Trace("ServiceResultException:" + ex.Message);
                logger.Error("Exception", ex.Message);
                return;
            }

            /*ManualResetEvent quitEvent = new ManualResetEvent(false);
            try
            {
                Console.CancelKeyPress += (sender, eArgs) =>
                {
                    quitEvent.Set();
                    eArgs.Cancel = true;
                };

            }
            catch
            {
            }

            // wait for timeout or Ctrl-C
            quitEvent.WaitOne(clientRunTime);

            // return error conditions
            if (session.KeepAliveStopped)
            {
                exitCode = ExitCode.ErrorNoKeepAlive;
                return;
            }

            exitCode = ExitCode.Ok;
            */
        }

        public static ExitCode ExitCode { get => exitCode; }

        private async Task ConsoleSampleClient()
        {
            logger.Info("1 - Create an Application Configuration.");
            exitCode = ExitCode.ErrorCreateApplication;

            ApplicationInstance application = new ApplicationInstance
            {
                ApplicationName = "UA Core Sample Client",
                ApplicationType = ApplicationType.Client,
                ConfigSectionName = Utils.IsRunningOnMono() ? "Opc.Ua.MonoSampleClient" : "Opc.Ua.SampleClient"
            };

            // load the application configuration.
            ApplicationConfiguration config = await application.LoadApplicationConfiguration(false);

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
                }
                config.CertificateValidator.CertificateValidation += new CertificateValidationEventHandler(CertificateValidator_CertificateValidation);
            }
            else
            {
                logger.Warn("missing application certificate, using unsecure connection.");
            }

            logger.Info("2 - Discover endpoints of {0}.", endpointURL);
            exitCode = ExitCode.ErrorDiscoverEndpoints;
            var selectedEndpoint = CoreClientUtils.SelectEndpoint(endpointURL, haveAppCertificate, 15000);
            logger.Info("    Selected endpoint uses: {0}",
                selectedEndpoint.SecurityPolicyUri.Substring(selectedEndpoint.SecurityPolicyUri.LastIndexOf('#') + 1));

            logger.Info("3 - Create a session with OPC UA server.");
            exitCode = ExitCode.ErrorCreateSession;
            var endpointConfiguration = EndpointConfiguration.Create(config);
            var endpoint = new ConfiguredEndpoint(null, selectedEndpoint, endpointConfiguration);
            session = await Session.Create(config, endpoint, false, "OPC UA Console Client", 60000, new UserIdentity(new AnonymousIdentityToken()), null);
            
            // register keep alive handler
            session.KeepAlive += Client_KeepAlive;
        }

        public void crowl(){
            logger.Info("4 - Browse the OPC UA server namespace.");
            exitCode = ExitCode.ErrorBrowseNamespace;
            ReferenceDescriptionCollection references;
            Byte[] continuationPoint;

            references = session.FetchReferences(ObjectIds.ObjectsFolder);

            session.Browse(
                null,
                null,
                ObjectIds.ObjectsFolder,
                0u,
                BrowseDirection.Forward,
                ReferenceTypeIds.HierarchicalReferences,
                true,
                (uint)NodeClass.Variable | (uint)NodeClass.Object | (uint)NodeClass.Method,
                out continuationPoint,
                out references);

            logger.Info(" DisplayName, BrowseName, NodeClass");
            
            foreach (var rd in references)
            { 
                logger.Info( rd.NodeId.NamespaceUri);
                ReferenceDescriptionCollection nextRefs;
                byte[] nextCp;
                session.Browse(
                    null,
                    null,
                    ExpandedNodeId.ToNodeId(rd.NodeId, session.NamespaceUris),
                    0u,
                    BrowseDirection.Forward,
                    ReferenceTypeIds.HierarchicalReferences,
                    true,
                    (uint)NodeClass.Variable | (uint)NodeClass.Object | (uint)NodeClass.Method,
                    out nextCp,
                    out nextRefs);

                Dictionary <string, Dictionary<string, VariableNode>> p = new Dictionary<string, Dictionary<string, VariableNode>>();
                foreach (var nextRd in nextRefs)
                {
                    logger.Info( "   + {0}, {1}, {2} ", nextRd.NodeId.NamespaceUri, nextRd.NodeId.ToString(), nextRd.NodeId.Identifier.ToString());

                }
            }

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
        public Task<StatusCodeCollection> asyncWrite(serverNode node, object value){

            WriteValue valueToWrite = new WriteValue();
            NodeId m_nodeId = new NodeId(node.serverIdentifier);
            valueToWrite.NodeId = m_nodeId;
            valueToWrite.AttributeId = Attributes.Value;
            try {
                valueToWrite.Value.Value = Convert.ChangeType( value, Type.GetType( node.systemType ));
            }
            catch (Exception e){
                logger.Error(e, "Error during conversion of node value");
                return badStatusCall();
            }
            
            valueToWrite.Value.StatusCode = StatusCodes.Good;

            WriteValueCollection valuesToWrite = new WriteValueCollection();
            valuesToWrite.Add(valueToWrite);
            
            return Task.Factory.FromAsync<StatusCodeCollection>(beginWriteWrapper,endWriteWrapper, valuesToWrite);

        }

        
        public void write(){
            logger.Info("9 - Reset counter");
            session.FetchNamespaceTables();

            // writing value sync :
             try
            {
                WriteValue valueToWrite = new WriteValue();
                NodeId m_nodeId = new NodeId("ns=3;s=\"ciao\"");
                valueToWrite.NodeId = m_nodeId;
                valueToWrite.AttributeId = Attributes.Value;
                valueToWrite.Value.Value = Convert.ToInt16(7);
                valueToWrite.Value.StatusCode = StatusCodes.Good;
                
                WriteValueCollection valuesToWrite = new WriteValueCollection();
                valuesToWrite.Add(valueToWrite);

                // write current value.
                StatusCodeCollection results = null;
                DiagnosticInfoCollection diagnosticInfos = null;


                // RequestHeader req = new RequestHeader();
                ResponseHeader resp =  session.Write(
                    null,
                    valuesToWrite,
                    out results,
                    out diagnosticInfos);
                
                ClientBase.ValidateResponse(results, valuesToWrite);
                ClientBase.ValidateDiagnosticInfos(diagnosticInfos, valuesToWrite);

                if (StatusCode.IsBad(results[0]))
                {
                    throw new ServiceResultException(results[0]);
                }
                logger.Info("Written OK :)");

            }
            catch (Exception exception)
            {
                logger.Error(exception,"Error during write value");
            }
            
        }

        public void subscribe(List<serverNode> serverNodes, List<EventHandler<MonItemNotificationArgs>> handlers){


            logger.Info("5 - Create a subscription with publishing interval of 1 second.");
            exitCode = ExitCode.ErrorCreateSubscription;
            var subscription = new Subscription(session.DefaultSubscription) { PublishingInterval = 1000 };

            logger.Info("6 - Add a list of items (server current time and status) to the subscription.");
            exitCode = ExitCode.ErrorMonitoredItem;
            var list = new List<MonitoredItem> {};

            logger.Warn("number of nodes : " + serverNodes.Count);

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
            // Adding all user defined notification handlers
            foreach(var handler in handlers) {
                MonitoredItemChanged += handler;
            }

            subscription.AddItems(list);

            logger.Info("7 - Add the subscription to the session.");
            exitCode = ExitCode.ErrorAddSubscription;
            session.AddSubscription(subscription);
            subscription.Create();

            logger.Info("8 - Running...Press Ctrl-C to exit...");
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
                    reconnectHandler.BeginReconnect(sender, ReconnectPeriod * 1000, Client_ReconnectComplete);
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
        public  string  endpointURL { get; set; }
        public  bool  autoAccept { get; set; }
        public  int stopTimeout { get; set; }

        public opcConfig(){
            endpointURL = "none";
            autoAccept = true;
            stopTimeout = -1; // infinite
        }
    }

}

