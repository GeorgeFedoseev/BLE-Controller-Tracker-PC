using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;

namespace gearvr_controller_tracker_pc
{
    public enum ControllerType {
        GearVRController,
        DaydreamController
    }

    class BaseController: IDisposable
    {
        public Action<GearVRController> OnSensorDataUpdated = (_) => { };


        protected ControllerType _controllerType;
        public ControllerType ControllerType {
            get {
                return _controllerType;
            }
        }   

        protected BluetoothLEDevice _bleDevice;
        protected ulong _bluetoothAddress;
        public ulong BluetoothAddress {
            get {
                return _bluetoothAddress;
            }
        }

        public string Name {
            get {
                return _bleDevice != null ? _bleDevice.Name : _bluetoothAddress.ToString();
            }
        }

        // CONNECTION
        protected volatile bool _wantToConnect = false;

        protected volatile bool _connected = false;
        public bool IsConnected {
            get {
                return _connected;
            }
        }
        
        protected volatile bool _connectionInProgress = false;


        // DATA
        protected GearVRControllerTrackingData _latestTrackingData = new GearVRControllerTrackingData();
        public GearVRControllerTrackingData LatestTrackingData {
            get {
                return _latestTrackingData;
            }
        }

        // METHODS

        public virtual void Initialize()
        {
            _wantToConnect = true;
        }

        public virtual void Disconnect()
        {
            _wantToConnect = false;
            _Disconnect();
        }

        protected virtual bool _Connect()
        {
            return false;
        }

        protected virtual void _Disconnect()
        {

        }

        // LOG
        protected void Log(string message) {
            Console.WriteLine($"[{Name}][Log] {message}");
        }

        protected void LogWarning(string message) {
            Console.WriteLine($"[{Name}][Warning] {message}");
        }

        protected void LogError(string message)
        {
            Console.WriteLine($"[{Name}][ERROR] {message}");
        }

        protected void LogException(Exception ex)
        {
            Console.WriteLine($"[{Name}][EXCEPTION] {ex.Message}\n{ex.StackTrace}");
        }



        public virtual void Dispose()
        {
            if (_connected) {
                _Disconnect();
            }

        }

       

    }
}
