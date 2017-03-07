using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Shared;
using Newtonsoft.Json;

namespace RMDeviceSample
{
    class Program
    {
        // String containing Hostname, Device Id & Device Key in one of the following formats:
        //  "HostName=<iothub_host_name>;DeviceId=<device_id>;SharedAccessKey=<device_key>"
        //  "HostName=<iothub_host_name>;CredentialType=SharedAccessSignature;DeviceId=<device_id>;SharedAccessSignature=SharedAccessSignature sr=<iot_host>/devices/<device_id>&sig=<token>&se=<expiry_time>";
        private static string DeviceConnectionString = "<replace>";

        private static DeviceClient Client = null;

        private static async Task OnDesiredPropertyChanged(TwinCollection desiredProperties, object userContext)
        {
            Console.WriteLine("desired property change:");
            Console.WriteLine(JsonConvert.SerializeObject(desiredProperties));

            Console.WriteLine("Sending current time as reported property");
            TwinCollection reportedProperties = new TwinCollection();
            reportedProperties["DateTimeLastDesiredPropertyChangeReceived"] = DateTime.Now;

            await Client.UpdateReportedPropertiesAsync(reportedProperties);
        }


        static void Main(string[] args)
        {
            string environmentConnectionString = Environment.GetEnvironmentVariable("IOTHUB_DEVICE_CONN_STR");
            if (!String.IsNullOrEmpty(environmentConnectionString))
            {
                DeviceConnectionString = environmentConnectionString;
            }

            try
            {
                Console.WriteLine("Checking for TransportType");
                var websiteHostName = Environment.GetEnvironmentVariable("WEBSITE_HOSTNAME");
                TransportType transportType = websiteHostName == null ? TransportType.Mqtt : TransportType.Mqtt_WebSocket_Only;
                Console.WriteLine($"Use TrasnportTyp: {transportType.ToString()}");

                Console.WriteLine("Connecting to hub");
                Client = DeviceClient.CreateFromConnectionString(DeviceConnectionString, transportType);
                Client.SetDesiredPropertyUpdateCallback(OnDesiredPropertyChanged, null).Wait();

                Console.WriteLine("Retrieving twin");
                var twinTask = Client.GetTwinAsync();
                twinTask.Wait();
                var twin = twinTask.Result;

                Console.WriteLine("initial twin value received:");
                Console.WriteLine(JsonConvert.SerializeObject(twin));

                Console.WriteLine("Sending app start time as reported property");
                TwinCollection reportedProperties = new TwinCollection();
                reportedProperties["DateTimeLastAppLaunch"] = DateTime.Now;

                Client.UpdateReportedPropertiesAsync(reportedProperties);
            }
            catch (AggregateException ex)
            {
                foreach (Exception exception in ex.InnerExceptions)
                {
                    Console.WriteLine();
                    Console.WriteLine("Error in sample: {0}", exception);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine("Error in sample: {0}", ex.Message);
            }
            Console.WriteLine("Waiting for Events.  Press enter to exit...");

            Console.ReadLine();
            Console.WriteLine("Exiting...");

        }
    }
}
