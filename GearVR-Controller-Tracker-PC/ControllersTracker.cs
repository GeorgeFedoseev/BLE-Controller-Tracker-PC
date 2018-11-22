using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace gearvr_controller_tracker_pc
{
    class ControllersTracker: IDisposable
    {

        private List<GearVRController> _controllers;

        private OSCTransmitter _transmitter;

        public void Run() {

            _controllers = new List<GearVRController>();

            // find controllers
            Console.WriteLine("Getting unpaired devices...");
            var foundAddresses = GearVRController.FindUnpairedControllersAddresses(Config.Main.controllersToTrack.Select(x => x.name).ToList());
            Console.WriteLine($"Found {foundAddresses.Count}/{Config.Main.controllersToTrack.Count}.");


            // connect to all found controllers
            foreach (var addr in foundAddresses) {
                var c = new GearVRController(foundAddresses[0]);                
                c.ConnectAsync();
                _controllers.Add(c);
            }

            // start transmitter
            _transmitter = new OSCTransmitter(_controllers);
            _transmitter.Start();
            
        }

        public void Dispose()
        {
            if (_transmitter != null) {
                _transmitter.Dispose();
                _transmitter = null;
            }

            foreach (var c in _controllers) {
                c.Dispose();
            }
        }
    }
}
