using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;

namespace console_ble
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Searching for GearVR controllers...");
            var controllers = GearVRController.FindPairedGearVRControllersAsync().GetAwaiter().GetResult();
            Console.WriteLine($"Found {controllers.Count} controller(-s)");

            if (controllers.Count > 0) {
                var c = controllers.First();

                while (true) {
                    Console.WriteLine("Try connecting...");
                    
                    var connected = c.Connect();
                    if (connected) {
                        break;
                    }

                    Thread.Sleep(1000);
                }                
            }

            Console.ReadKey();
        }


    }
}
