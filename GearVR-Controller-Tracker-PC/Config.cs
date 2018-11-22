using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace gearvr_controller_tracker_pc
{
    [Serializable]
    public class Config
    {
        public string oscReceiverIPAddress;
        public int oscReceiverPort;
        public List<ControllerConfig> controllersToTrack;


        private static Config _instance;
        public static Config Main {
            get {
                if (_instance == null) {
                    _instance = LoadConfig();
                }
                return _instance;
            }
        }


        public Config() { }

        public static Config LoadConfig()
        {
            Console.WriteLine("Loading config...");

            var configPath = Const.CONFIG_FILENAME;

            string jsonStr;
            try {
                jsonStr = File.ReadAllText(configPath);
            }
            catch (FileNotFoundException ex) {
                Console.WriteLine($"Config not found at: {configPath}: {ex.Message}");
                return null;
            }
            catch (Exception ex) {
                Console.WriteLine($"Failed to read config file: {ex.Message}");
                return null;
            }

            Config _config = null;
            var jsonSettings = new JsonSerializerSettings();
            jsonSettings.DateFormatString = "yyyy-MM-dd HH:mm:ss";
            jsonSettings.DateTimeZoneHandling = DateTimeZoneHandling.Local;

            try {
                _config = JsonConvert.DeserializeObject<Config>(jsonStr, jsonSettings);
            }
            catch (Exception ex) {
                Console.WriteLine($"Failed to deserialize config file: {ex.Message} {ex.StackTrace}");
            }


            return _config;
        }

    }

    [Serializable]
    public class ControllerConfig
    {
        public string name;
        public string oscAddress;

    }

}