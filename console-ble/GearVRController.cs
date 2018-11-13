using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;

namespace console_ble
{
    class GearVRController
    {

        static float SEARCH_TIME_SECONDS = 3;

        static Guid UUID_CUSTOM_SERVICE = Guid.Parse("4f63756c-7573-2054-6872-65656d6f7465");
        static Guid UUID_WRITE_CHARACTERISTIC = Guid.Parse("c8c51726-81bc-483b-a052-f7a14ea3d282");
        static Guid UUID_NOTIFY_CHARACTERISTIC = Guid.Parse("c8c51726-81bc-483b-a052-f7a14ea3d281");


        private BluetoothLEDevice _device;


        public GearVRController() {
        }

        public async Task ConnectAsync() {


            // Pair

            Console.WriteLine($"Pairing with {_device.Name}...");

            bool paired = false;

            _device.DeviceInformation.Pairing.Custom.PairingRequested += (s, args) => {
                Console.WriteLine($"Pairing requested, Kind: {args.PairingKind}");

                switch (args.PairingKind) {
                    case DevicePairingKinds.ConfirmOnly:
                        // Windows itself will pop the confirmation dialog as part of "consent" if this is running on Desktop or Mobile
                        // If this is an App for 'Windows IoT Core' or a Desktop and Console application
                        // where there is no Windows Consent UX, you may want to provide your own confirmation.
                        args.Accept();
                        break;
                }
            };

            while (!paired) {

                Console.WriteLine("Try pairing...");

                var prslt = await _device.DeviceInformation.Pairing.Custom.PairAsync(DevicePairingKinds.ConfirmOnly);
                Console.WriteLine($"Device pairing result: {prslt.Status}");

                if (prslt.Status == DevicePairingResultStatus.Paired
                   || prslt.Status == DevicePairingResultStatus.AlreadyPaired) 
                {
                    paired = true;                    
                }

                Thread.Sleep(500);
            }



            Console.WriteLine("Get service...");
            // services
            var services = await _device.GetGattServicesForUuidAsync(UUID_CUSTOM_SERVICE);

            Console.WriteLine($"Get services status: {services.Status}, count: {services.Services.Count}, protocol error: {services.ProtocolError}");

            var service = services.Services.First();


            Console.WriteLine($"Service {service.Uuid} characteristics ({service.GetAllCharacteristics().Count}):");
            var characteristics = await service.GetCharacteristicsAsync();
            Console.WriteLine($"Get service characteristics status: {characteristics.Status}");

            foreach (var c in characteristics.Characteristics) {
                Console.WriteLine(c.Uuid);     
            }

            var notify_chars = await service.GetCharacteristicsForUuidAsync(UUID_NOTIFY_CHARACTERISTIC);
            var notify_c = notify_chars.Characteristics.First();

            var write_chars = await service.GetCharacteristicsForUuidAsync(UUID_WRITE_CHARACTERISTIC);
            var write_c = write_chars.Characteristics.First();

            Console.WriteLine($"Found {write_chars.Characteristics.Count} write characteristics");
            Console.WriteLine($"Found {notify_chars.Characteristics.Count} notify characteristics");

            Console.WriteLine("Subscribe to characteristic notifications...");

            Console.WriteLine($"Notify characteristic supports notify: {notify_c.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Notify)}");


            GattCommunicationStatus subscribeResult = GattCommunicationStatus.Unreachable;

            while (subscribeResult != GattCommunicationStatus.Success) {
                subscribeResult = await notify_c.WriteClientCharacteristicConfigurationDescriptorAsync(
                        GattClientCharacteristicConfigurationDescriptorValue.Notify);
                Console.WriteLine($"Subscribe characteristic write result: {subscribeResult}");
                Thread.Sleep(1000);
            }
                
            
            

            if (subscribeResult == GattCommunicationStatus.Success) {

                notify_c.ValueChanged += (s, args) => {
                    Console.WriteLine("changed");
                };

                // initial kick

                var writer = new Windows.Storage.Streams.DataWriter();
                short val = 0x0100;
                writer.WriteInt16(val);
                var writeResult = await write_c.WriteValueAsync(writer.DetachBuffer());
                Console.WriteLine($"Write kick result: {writeResult}");
            }



            //
            //foreach(var c in chars) {
            //    var _c = c;
            //    c.ValueChanged += (s, args) => {
            //        Console.WriteLine($"{_c.Uuid}: {args.CharacteristicValue}");
            //    };
            //}


        }
        

        public static List<GearVRController> FindGearVRControllers() {
            var result = new List<GearVRController>();

            

            var _bleWatcher = new BluetoothLEAdvertisementWatcher {
                ScanningMode = BluetoothLEScanningMode.Active
            };


            _bleWatcher.Received += async (w, btAdv) => {

                //Console.WriteLine("Found " + btAdv.Advertisement.LocalName);

                if (!btAdv.Advertisement.LocalName.Contains("Gear VR Controller")) {
                    return;
                }

                if (result.Any(x => x._device.BluetoothAddress == btAdv.BluetoothAddress)) {
                    // already added
                    return;
                }

                var op = BluetoothLEDevice.FromBluetoothAddressAsync(btAdv.BluetoothAddress);
                while (op.Status == Windows.Foundation.AsyncStatus.Started) {
                    Thread.Sleep(100);
                }
                var device = op.GetResults();

                if (device == null) {
                    return;
                }
                

                Console.WriteLine($"Found {btAdv.Advertisement.LocalName}");

                var controller = new GearVRController();
                controller._device = device;

                result.Add(controller);

                

                

                // CHARACTERISTICS!!
                //var characs = await gatt.Services.Single(s => s.Uuid == SAMPLESERVICEUUID).GetCharacteristicsAsync();
                //var charac = characs.Single(c => c.Uuid == SAMPLECHARACUUID);
                //await charac.WriteValueAsync(SOMEDATA);
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

       

        
    }
}
