using OpcProxyCore;
using Newtonsoft.Json.Linq;
using OpcProxyClient;
using OpcGrpcConnect;
using OpcInfluxConnect;


namespace OPC_Proxy
{
    class Program
    {
        static int Main(string[] args)
        {

            serviceManager man = new serviceManager(args);            

            HttpImpl opcHttpConnector = new HttpImpl();
            InfluxImpl influx = new InfluxImpl();

            man.addConnector(opcHttpConnector);
            man.addConnector(influx);

            man.run();

            return (int)OPCclient.ExitCode;           

            // db.Dispose();
        }
        
    }
}
