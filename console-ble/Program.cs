using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;

namespace console_ble
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Searching for GearVR controllers...");
            var controllers = GearVRController.FindGearVRControllers();
            Console.WriteLine($"Found {controllers.Count} controller(-s)");

            var c = controllers.First();
            c.ConnectAsync();


            Console.ReadKey();
        }


    }
}
