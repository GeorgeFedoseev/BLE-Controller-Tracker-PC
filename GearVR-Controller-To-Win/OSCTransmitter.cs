using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace gearvr_controller_to_win
{
    class OSCTransmitter
    {
        private List<GearVRController> _controllers;

        public OSCTransmitter(List<GearVRController> controllers) {
            _controllers = controllers;
        }

        public void Start() {
            // start transmitting data
            foreach (var c in _controllers) {
                
                c.OnSensorDataUpdated += (sender) => {
                    TransmitDataForController(sender);
                };
                
            }
        }

        private void TransmitDataForController(GearVRController controller) {

        }
    }
}
