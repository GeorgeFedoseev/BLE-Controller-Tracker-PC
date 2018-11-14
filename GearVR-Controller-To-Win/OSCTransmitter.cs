using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using SharpOSC;

namespace gearvr_controller_to_win
{
    class OSCTransmitter: IDisposable
    {
        private List<GearVRController> _controllers;
        private UDPSender _oscClient;

        public OSCTransmitter(List<GearVRController> controllers) {
            _controllers = controllers;
        }

        public void Start() {
            // setup OSC server
            _oscClient = new UDPSender(Config.Main.oscReceiverIPAddress, Config.Main.oscReceiverPort);
            

            // start transmitting data
            foreach (var c in _controllers) {
                
                c.OnSensorDataUpdated += (sender) => {
                    TransmitDataForController(sender);
                };
                
            }
        }

        private void TransmitDataForController(GearVRController controller) {
            
            Console.WriteLine($"Transmit data for controller {controller.Name}");

            ControllerConfig controllerConfig = Config.Main.controllersToTrack.Where(c => c.name == controller.Name).FirstOrDefault();
            if (controllerConfig == null) {
                return;
                //throw new Exception($"Cant find config for controller {controller.Name}");
            }

            try {
                _oscClient.Send(new OscMessage(controllerConfig.oscAddress, JsonConvert.SerializeObject(controller.trackingData, Formatting.None)));
            }
            catch(Exception ex) {
                Console.WriteLine($"Failed to send OSC: {ex.Message}");
            }
            
        }

        public void Dispose() {
            if (_oscClient != null) {
                _oscClient.Close();
            }
        }
    }
}
