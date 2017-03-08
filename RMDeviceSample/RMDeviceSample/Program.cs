using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Shared;
using Newtonsoft.Json;
using System.Dynamic;
using System.Threading;

namespace RMDeviceSample
{
    public class RemoteMonitorTelemetryData
    {
        public string DeviceId { get; set; }
        public double Temperature { get; set; }
        public double Humidity { get; set; }
    }

    class Program
    {
        // String containing Hostname, Device Id & Device Key in one of the following formats:
        //  "HostName=<iothub_host_name>;DeviceId=<device_id>;SharedAccessKey=<device_key>"
        //  "HostName=<iothub_host_name>;CredentialType=SharedAccessSignature;DeviceId=<device_id>;SharedAccessSignature=SharedAccessSignature sr=<iot_host>/devices/<device_id>&sig=<token>&se=<expiry_time>";
        //private static string HostName = "<replace>";
        //private static string DeviceID = "<replace>";
        //private static string PrimaryAuthKey = "<replace>";
        private static string ObjectTypePrefix = "";// Replace with your prefix
        private static string HostName = "LocalRM51fc6.azure-devices.net";
        private static string DeviceID = "testdeviceceli1";
        private static string PrimaryAuthKey = "NXx3nn+V+jKaWOxttb65xg==";
        
        private static DeviceClient Client = null;
        private static CancellationTokenSource cts = new CancellationTokenSource();

        private static bool TelemetryActive = true;
        private static uint _telemetryIntervalInSeconds = 15;
        public static uint TelemetryIntervalInSeconds
        {
            get
            {
                return _telemetryIntervalInSeconds;
            }
            set
            {
                _telemetryIntervalInSeconds = value;
                TelemetryActive = _telemetryIntervalInSeconds > 0;
            }
        }

        private static Random rnd = new Random();

        private static async Task OnDesiredPropertyChanged(TwinCollection desiredProperties, object userContext)
        {
            Console.WriteLine("desired property change:");
            Console.WriteLine(JsonConvert.SerializeObject(desiredProperties));

            Console.WriteLine("Sending current time as reported property");
            TwinCollection reportedProperties = new TwinCollection();
            reportedProperties["DateTimeLastDesiredPropertyChangeReceived"] = DateTime.Now;

            await Client.UpdateReportedPropertiesAsync(reportedProperties);
        }

        private static async Task SendDeviceInfoAsync(CancellationToken token, Func<object, Task> sendMessageAsync)
        {
            dynamic device = new ExpandoObject();
            device.DeviceProperties = new ExpandoObject();

            // Device basic properties
            device.DeviceProperties.DeviceID = DeviceID;
            device.DeviceProperties.HubEnabledState = true;
            device.DeviceProperties.CreatedTime = DateTime.UtcNow;
            device.DeviceProperties.DeviceState = "normal";
            device.DeviceProperties.UpdatedTime = null;

            // Your device information here
            device.DeviceProperties.Manufacturer = "MyCompany";
            device.DeviceProperties.ModelNumber = "MyModel";
            device.DeviceProperties.SerialNumber = "MySerial";
            device.DeviceProperties.FirmwareVersion = "1.0";
            device.DeviceProperties.Platform = "MyPlatfrom";
            device.DeviceProperties.Processor = "I3";
            device.DeviceProperties.InstalledRAM = "64MB";

            // Simlated localtion
            device.DeviceProperties.Latitude = 47.659159;
            device.DeviceProperties.Longitude = -122.141515;

            // Telemery data descriptor
            device.Telemetry = new List<dynamic>();
            device.Telemetry.Add(new { Name = "Temperature", DisplayName= "Temperature", Type = "double" });
            device.Telemetry.Add(new { Name= "Humidity", DisplayName= "Humidity", Type = "double" });

            // Message type for RemoteMonitoring
            device.Version = "1.0";
            device.ObjectType = "DeviceInfo";

            // Remove the system properties from a device, to better emulate the behavior of real devices when sending device info messages.
            device.SystemProperties = null;

            if (!token.IsCancellationRequested)
            {
                await sendMessageAsync(device);
            }
        }

        public static async Task SendMonitorDataAsync(CancellationToken token, Func<object, Task> sendMessageAsync)
        {
            var monitorData = new RemoteMonitorTelemetryData();
            while (!token.IsCancellationRequested)
            {
                if (TelemetryActive)
                {
                    // Build simlated telemerty data.
                    monitorData.DeviceId = DeviceID;
                    monitorData.Temperature = Math.Round(rnd.NextDouble() * 100, 1);
                    monitorData.Humidity = Math.Round(rnd.NextDouble() * 100, 1);

                    await sendMessageAsync(monitorData);
                }
                await Task.Delay(TimeSpan.FromSeconds(TelemetryIntervalInSeconds), token);
            }
        }

        public static async Task SendEventAsync(Guid eventId, dynamic eventData)
        {
            string objectType = GetObjectType(eventData);
            if (!string.IsNullOrWhiteSpace(objectType) && !string.IsNullOrEmpty(ObjectTypePrefix))
            {
                eventData.ObjectType = ObjectTypePrefix + objectType;
            }

            var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(eventData));
            var message = new Microsoft.Azure.Devices.Client.Message(bytes);
            message.Properties["EventId"] = eventId.ToString();

            if (Client != null)
            {
                try
                {
                    await Client.SendEventAsync(message);
                }
                catch(Exception ex)
                {
                    Console.Write($"Exception raised while device {DeviceID} trying to send events: {ex.Message}");
                }
                
            }
        }

        private static string GetObjectType(dynamic eventData)
        {
            if (eventData == null)
            {
                throw new ArgumentNullException("eventData");
            }

            var propertyInfo = eventData.GetType().GetProperty("ObjectType");
            if (propertyInfo == null)
            {
                return string.Empty;
            }

            var value = propertyInfo.GetValue(eventData, null);
            return value == null ? string.Empty : value.ToString();
        }

        static void Main(string[] args)
        {
            string DeviceConnectionString;
            string environmentConnectionString = Environment.GetEnvironmentVariable("IOTHUB_DEVICE_CONN_STR");
            if (!String.IsNullOrEmpty(environmentConnectionString))
            {
                DeviceConnectionString = environmentConnectionString;
            }
            else
            {
                var authMethod = new Microsoft.Azure.Devices.Client.DeviceAuthenticationWithRegistrySymmetricKey(DeviceID, PrimaryAuthKey);
                DeviceConnectionString = Microsoft.Azure.Devices.Client.IotHubConnectionStringBuilder.Create(HostName, authMethod).ToString();
            }

            try
            {
                Console.WriteLine("Checking for TransportType");
                var websiteHostName = Environment.GetEnvironmentVariable("WEBSITE_HOSTNAME");
                TransportType transportType = websiteHostName == null ? TransportType.Mqtt : TransportType.Mqtt_WebSocket_Only;
                Console.WriteLine($"Use TransportType: {transportType.ToString()}");

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

                Console.WriteLine("Sending device infomation to RM");
                var deviceTask = SendDeviceInfoAsync(cts.Token, async (object eventData) =>
                {
                    await SendEventAsync(Guid.NewGuid(), eventData);
                });
                deviceTask.Wait();

                var monitorTask = SendMonitorDataAsync(cts.Token, async (object eventData) =>
                {
                    await SendEventAsync(Guid.NewGuid(), eventData);
                });
                monitorTask.Wait();

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
            cts.Cancel();
            Console.WriteLine("Exiting...");

        }
    }
}
