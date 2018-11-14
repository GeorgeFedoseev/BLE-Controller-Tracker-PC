using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace gearvr_controller_to_win
{
    [Serializable]
    class GearVRControllerTrackingData
    {
        public bool homeButton = false;
        public bool backButton = false;
        public bool volumeDownButton = false;
        public bool volumeUpButton = false;
        public bool triggerButton = false;
        public bool touchpadButton = false;
        public bool touchpadPressed = false;
        public int touchpadX = 0;
        public int touchpadY = 0;

        public float[] quaternion;
    }
}
