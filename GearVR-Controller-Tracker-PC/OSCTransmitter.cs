using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using SharpOSC;
using System.Diagnostics;

namespace gearvr_controller_tracker_pc
{
    class OSCTransmitter: IDisposable
    {
        private List<BaseController> _controllers = new List<BaseController>();
        private UDPSender _oscClient;

        public OSCTransmitter() {
            
        }

        public void Start() {
            // setup OSC server
            _oscClient = new UDPSender(Config.Main.oscReceiverIPAddress, Config.Main.oscReceiverPort);            

            
        }

        public void UpdateControllersList(List<BaseController> updatedControllers) {
            

            // start transmitting data for new controllers
            foreach (var c in updatedControllers.Where(x => !_controllers.Any(c => x.BluetoothAddress == c.BluetoothAddress))) {
                Console.WriteLine($"Start transmitting OSC for controller {c.Name}");
                c.OnSensorDataUpdated += (sender) => {
                    TransmitDataForController(sender);
                };
            }

            _controllers = updatedControllers.ToList();
        }

        DateTime _lastTimeSent = DateTime.MinValue;
        private void TransmitDataForController(GearVRController controller) {
           
            if ((DateTime.Now - _lastTimeSent).TotalMilliseconds > 0) {
                try {                    
                    var trackingData = controller.LatestTrackingData;

                    var serializedStr = JsonConvert.SerializeObject(trackingData, Formatting.None);
                    
                    _oscClient.Send(new OscMessage($"/{controller.Name}",
                        serializedStr,
                        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    ));                    
                }
                catch (Exception ex) {
                    Console.WriteLine($"Failed to send OSC: {ex.Message}");
                }

                _lastTimeSent = DateTime.Now;
            }
        }

        public void Dispose() {
            if (_oscClient != null) {
                _oscClient.Close();
            }
        }
    }
}
