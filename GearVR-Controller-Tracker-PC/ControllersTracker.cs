using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth.Advertisement;

namespace controller_tracker
{
    class ControllersTracker : IDisposable
    {
        private static NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();


        private static readonly float SEARCH_TIME_SECONDS = 3;

        private List<BaseController> _discoveredControllers;
        private OSCTransmitter _transmitter;

        private Thread _controllersSearchingThread;

        public void Run() {
            
            StartSearchingForControllers();

            // start transmitter
            _transmitter = new OSCTransmitter();
            _transmitter.Start();
        }


        // CONSTANTLY SEARCH FOR CONTROLLERS

        void StartSearchingForControllers() {
            if (_controllersSearchingThread != null) {
                logger.Info($"Warning: Trying to start controllers searching thread. Already started before.");
                return;
            }

            _discoveredControllers = new List<BaseController>();

            logger.Info("Start searching for controllers...");

            _controllersSearchingThread = new Thread(FindControllersWorker);
            _controllersSearchingThread.IsBackground = true;
            _controllersSearchingThread.Start();
        }

        void FindControllersWorker(){
            var foundUnpairedControllerAddresses = FindUnpairedControllersAddresses();
            foreach (var nameAddressTuple in foundUnpairedControllerAddresses) {
                var deviceName = nameAddressTuple.Item1;
                var bluetoothAddress = nameAddressTuple.Item2;

                if (_discoveredControllers.Any(c => c.BluetoothAddress == nameAddressTuple.Item2)) {
                    // we already know about this controller
                    logger.Info($"Found {deviceName} - Already added before");
                    continue;
                }

                logger.Info($"Found {deviceName} - NEW");

                if (deviceName.Contains("Gear VR Controller")) {
                    // create and add new gear vr controller
                    var gearVRController = new GearVRController(bluetoothAddress);
                    gearVRController.Initialize();
                    _discoveredControllers.Add(gearVRController);

                    OnNewControllerAdded(gearVRController);
                }
                // TODO: elseif Daydream



            }
        }

        public static List<Tuple<string, ulong>> FindUnpairedControllersAddresses(List<string> acceptNames = null)
        {
            var result = new List<Tuple<string, ulong>>();

            var _bleWatcher = new BluetoothLEAdvertisementWatcher {
                ScanningMode = BluetoothLEScanningMode.Active
            };

            _bleWatcher.Received += (w, btAdv) => {
                if (acceptNames != null) {
                    if (!acceptNames.Any(x => btAdv.Advertisement.LocalName == x)) {
                        return;
                    }
                }
                else {
                    if (!btAdv.Advertisement.LocalName.Contains("Gear VR Controller")) {
                        return;
                    }
                }

                if (result.Any(x => x.Item2 == btAdv.BluetoothAddress)) {
                    // already added
                    return;
                }

               // logger.Info("Found " + btAdv.Advertisement.LocalName);

                result.Add(Tuple.Create(btAdv.Advertisement.LocalName, btAdv.BluetoothAddress));
            };

            _bleWatcher.Start();

            var sw = new Stopwatch();
            sw.Start();
            while (result.Count == 0 || sw.Elapsed.TotalSeconds < SEARCH_TIME_SECONDS) {
                Thread.Sleep(100);
            }
            _bleWatcher.Stop();

            return result;
        }


        // EVENTS

        private void OnNewControllerAdded(BaseController controller) {
            _transmitter.UpdateControllersList(_discoveredControllers);
        }



        public void Dispose()
        {
            if (_transmitter != null) {
                _transmitter.Dispose();
                _transmitter = null;
            }

            if (_controllersSearchingThread != null && _controllersSearchingThread.IsAlive) {
                _controllersSearchingThread.Abort();
                _controllersSearchingThread = null;
            }            

            foreach (var c in _discoveredControllers) {
                c.Dispose();
            }
            _discoveredControllers.Clear();
        }
    }
}
