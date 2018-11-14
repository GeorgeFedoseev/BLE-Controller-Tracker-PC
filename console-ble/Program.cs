using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Windows.Devices.Bluetooth;

namespace console_ble
{
    class Program
    {
        [DllImport("Kernel32")]
        private static extern bool SetConsoleCtrlHandler(EventHandler handler, bool add);

        private delegate bool EventHandler(CtrlType sig);
        static EventHandler _handler;

        enum CtrlType
        {
            CTRL_C_EVENT = 0,
            CTRL_BREAK_EVENT = 1,
            CTRL_CLOSE_EVENT = 2,
            CTRL_LOGOFF_EVENT = 5,
            CTRL_SHUTDOWN_EVENT = 6
        }

        

        static void Main(string[] args)
        {
            _handler += new EventHandler(ConsoleExitHandler);
            SetConsoleCtrlHandler(_handler, true);



            Console.WriteLine("Getting unpaired devices...");
            var unpaired = GearVRController.FindUnpairedControllersAddresses();
            Console.WriteLine("Done.");

            if (unpaired.Count > 0) {
                var c = new GearVRController(unpaired[0]);
                c.Connect();
            }
            
            // pair all controllers
            //foreach (var adr in addresses) {
            //    Console.WriteLine($"Pairing device {adr}...");

            //    var bleDevice = BluetoothLEDevice.FromBluetoothAddressAsync(adr).AsTask().GetAwaiter().GetResult();
            //    bleDevice.DeviceInformation.Pairing.Custom.PairingRequested += (s, a) => { a.Accept(); };

            //    //var unpairRes = bleDevice.DeviceInformation.Pairing.UnpairAsync().AsTask().GetAwaiter().GetResult();
            //    //Console.WriteLine($"UnPairing result: {unpairRes.Status}");

            //    ////while (!bleDevice.DeviceInformation.Pairing.CanPair) {
            //    //    Thread.Sleep(5000);
            //    ////}

            //    var pairRes = bleDevice.DeviceInformation.Pairing.PairAsync().AsTask().GetAwaiter().GetResult();
            //    Console.WriteLine($"Pairing result: {pairRes.Status}");
            //}

            //var paired = GearVRController.FindPairedGearVRControllersAsync().GetAwaiter().GetResult();

            //Console.WriteLine($"Devices paired: {paired.Count}");

            //if (paired.Count > 0) {
            //    var c = paired[0];
            //    c.Connect();
            //}




            Console.ReadKey();
        }

     


        private static bool ConsoleExitHandler(CtrlType sig)
        {
            Console.WriteLine("Exiting system due to external CTRL-C, or process kill, or shutdown");
            

            //do your cleanup here
            //Thread.Sleep(5000); //simulate some cleanup delay

            Console.WriteLine("Cleanup complete");

            //allow main to run off
            //exitSystem = true;

            //shutdown right away so there are no lingering threads
            Environment.Exit(-1);

            return true;
        }



    }
}
