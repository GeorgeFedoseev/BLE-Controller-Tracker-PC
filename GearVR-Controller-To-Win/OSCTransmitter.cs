using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using SharpOSC;
using System.Diagnostics;

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


        DateTime _lastTimeSent = DateTime.MinValue;
        private void TransmitDataForController(GearVRController controller) {
            
            //Console.WriteLine($"Transmit data for controller {controller.Name}");

            ControllerConfig controllerConfig = Config.Main.controllersToTrack.Where(c => c.name == controller.Name).FirstOrDefault();
            if (controllerConfig == null) {
                return;
                //throw new Exception($"Cant find config for controller {controller.Name}");
            }

            if ((DateTime.Now - _lastTimeSent).TotalMilliseconds > 0) {
                try {
                    //var quaternionStr = string.Join("|", controller.trackingData.quaternion);
                    //var stopwatch = new Stopwatch();
                    //stopwatch.Start();
                    var trackingData = controller.trackingData;


                    var serializedStr = JsonConvert.SerializeObject(trackingData, Formatting.None);
                    //Console.WriteLine($"Time to serialize json: {stopwatch.Elapsed.TotalMilliseconds} ms"); // < 0.1ms

                    _oscClient.Send(new OscMessage(controllerConfig.oscAddress,
                        serializedStr,
                        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                        ));

                    //_oscClient.Send(new OscMessage(controllerConfig.oscAddress, 
                    //    controller.trackingData.quaternion[0],
                    //    controller.trackingData.quaternion[1],
                    //    controller.trackingData.quaternion[2],
                    //    controller.trackingData.quaternion[3]
                    //    ));
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
