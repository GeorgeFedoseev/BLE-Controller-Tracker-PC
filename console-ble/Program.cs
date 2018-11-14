using System;
using System.Collections.Generic;
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


        static List<GearVRController> _controllers;

        static void Main(string[] args)
        {
            _handler += new EventHandler(ConsoleExitHandler);
            SetConsoleCtrlHandler(_handler, true);


            Console.WriteLine("Searching for GearVR controllers...");
            _controllers = GearVRController.FindPairedGearVRControllersAsync().GetAwaiter().GetResult();
            Console.WriteLine($"Found {_controllers.Count} controller(-s)");

            if (_controllers.Count > 0) {
                var c = _controllers.First();
                
                c.Connect();
                
                              
            }

            Console.ReadKey();
        }


        private static bool ConsoleExitHandler(CtrlType sig)
        {
            Console.WriteLine("Exiting system due to external CTRL-C, or process kill, or shutdown");

            _controllers.ForEach(c => c.Dispose());

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
