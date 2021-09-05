using Plugin.BLE;
using Plugin.BLE.Abstractions.Contracts;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xamarin.Essentials;
using Xamarin.Forms;

namespace App1
{
    public partial class MainPage : ContentPage
    {
        private IBluetoothLE _ble;
        private IAdapter _adapter;
        private string _devices = "";
        private Data data;

        public MainPage()
        {
            data = new Data();
            data.Devices = "";
            BindingContext = data;
            InitializeComponent();            
        }

        private async void Button_Clicked(object sender, EventArgs e)
        {
            _ble = CrossBluetoothLE.Current;
            _adapter = CrossBluetoothLE.Current.Adapter;

            // włączona lokalizacja i uprawnienie jest wymagane
            var stat = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();

            if(stat != PermissionStatus.Granted)
            {
                await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
            }

            if (_ble.State == BluetoothState.On)
            {
                _adapter.ScanMode = ScanMode.Balanced; // moc sygnału (?)
                _adapter.ScanTimeout = 10000; // czas wyszukiwania
                _adapter.DeviceDiscovered += (s, a) => DeviceAdd(a.Device);
                _adapter.ScanTimeoutElapsed += async (s, a) => { await TimeoutElapsed(); } ;

                if (!_ble.Adapter.IsScanning)
                {
                    try
                    {
                        await _adapter.StartScanningForDevicesAsync();
                    }
                    catch (Exception w)
                    {
                        Console.WriteLine(w.Message);
                    }
                }
            }
        }
        
        /// <summary>
        /// Wyświetla urządzenie w labelu po dodaniu
        /// </summary>
        /// <param name="device"></param>
        void DeviceAdd(IDevice device)
        {
            _devices += "\n" + device.Name;
            data.DevicesObjects.Add(device);
            data.Devices = _devices;
        }

        /// <summary>
        /// Funkcja wykonywana po zakończeniu wyszukiwania
        /// </summary>
        /// <returns></returns>
        async Task TimeoutElapsed()
        {
            try
            {
                // wybranie urządzenia i połączenie
                var device = data.DevicesObjects.FirstOrDefault(x => (x?.Name?.StartsWith("iNode-G10") ?? false)); 
                await _adapter.ConnectToDeviceAsync(device);

                var service = await device.GetServiceAsync(Guid.Parse("0000CB4A-5EFD-45BE-B5BE-158DF376D8AD"));
                var charac = await service.GetCharacteristicAsync(Guid.Parse("0000CB4C-5EFD-45BE-B5BE-158DF376D8AD"));

                // konwersja daty na timestamp
                DateTime now = DateTime.Now;
                var unixTimeStamp = (int)(DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1, 0, 0, 0))).TotalSeconds;
                byte[] arrayNow = new byte[] { (byte)(unixTimeStamp), (byte)(unixTimeStamp >> 8), (byte)(unixTimeStamp >> 16), (byte)(unixTimeStamp >> 24)};
                
                // lista na wysyłane dane
                List<byte> byteList = new List<byte>();

                // 1. wysyła aktualny czas
                byteList.Add(0x04);
                byteList.Add(0x01);
                byteList.AddRange(arrayNow);

                await charac.WriteAsync(byteList.ToArray());

                // 2. wyłącza archiwizację danych
                byteList.Clear();
                byteList.Add(0x02);
                byteList.Add(0x01);

                await charac.WriteAsync(byteList.ToArray());

                // 3. sposób odczytu danych
                byteList.Clear();
                byteList.Add(0x0B);
                byteList.Add(0x01);

                await charac.WriteAsync(byteList.ToArray());

                // 4. odczyt adresu ostatniego rekordu z danymi
                byteList.Clear();
                byteList.Add(0x07);
                byteList.Add(0x01);
                byteList.Add(0x10);
                byteList.Add(0x00);

                await charac.WriteAsync(byteList.ToArray());
                byte[] last_addrArray = await charac.ReadAsync();

                // 5. odczyt ilości zapisanych rekordów
                byteList.Clear();
                byteList.Add(0x07);
                byteList.Add(0x01);
                byteList.Add(0x12);
                byteList.Add(0x00);

                await charac.WriteAsync(byteList.ToArray());

                byte[] len_recordsArray = await charac.ReadAsync();

                // konwersja ostatniego adresu z danymi i ilości danych na inta
                var last_addrINT = (last_addrArray[1] << 8) + (last_addrArray[0]);
                var len_recordsINT = (len_recordsArray[1] << 8) + (len_recordsArray[0]);

                // 6. obliczenie ilości bajtów do odczytania
                var len_bytesINT = len_recordsINT > 8192 ? 65536 : (8 * len_recordsINT) % 65536;

                // 7. obliczenie początkowego adresu
                var start_addr = (last_addrINT - len_bytesINT) & 0xFFFF;

                // konwersja ilości bajtów i początkowego adresu do byte[]
                var start_addrArray = new byte[] { (byte)(start_addr), (byte)(start_addr >> 8)};
                var len_bytesArray = new byte[] { (byte)(len_bytesINT), (byte)(len_bytesINT >> 8)};

                // 8. ustawia adres od jakiego mają zostać odczytane dane i ilość bajtów
                byteList.Clear();
                byteList.Add(0x03);
                byteList.Add(0x01);
                byteList.Add(start_addrArray[0]);
                byteList.Add(start_addrArray[1]);
                byteList.Add(len_bytesArray[0]);
                byteList.Add(len_bytesArray[1]);

                await charac.WriteAsync(byteList.ToArray());

                // znalezienie deskryptora configu
                var EEPROMPAGECharac = await service.GetCharacteristicAsync(Guid.Parse("0000CB4D-5EFD-45BE-B5BE-158DF376D8AD"));
                var descriptors = await EEPROMPAGECharac.GetDescriptorsAsync();
                var clientConfigDescr = descriptors.Last();

                // 9. włączenie trybu notyfikacji
                byteList.Clear();
                byteList.Add(0x01);
                byteList.Add(0x00);

                await clientConfigDescr.WriteAsync(byteList.ToArray());

                // 10. wysłanie rozkazu rozpoczęcia przesyłania i odebranie danych z notyfikacji
                byteList.Clear();
                byteList.Add(0x05);
                byteList.Add(0x01);

                var listOfRecords = new List<byte[]>();
                int length = 0;

                EEPROMPAGECharac.ValueUpdated += (s, a) => 
                {
                    length += a.Characteristic.Value.Length;
                    listOfRecords.Add(a.Characteristic.Value);
                    if (length >= len_bytesINT)
                    {
                        EEPROMPAGECharac.StopUpdatesAsync();

                        // wyświetlenie rekordów
                        ParseRecords(listOfRecords);
                    }
                };

                await EEPROMPAGECharac.StartUpdatesAsync();
                await charac.WriteAsync(byteList.ToArray());

                // 12. wyczyszczenie rekordów i ustawienie aktualnego czasu
                //byteList.Clear();
                //byteList.Add(0x04);
                //byteList.Add(0x02);
                //byteList.AddRange(arrayNow);

                //await charac.WriteAsync(byteList.ToArray());
                // 12

                // 13. włączenie archiwizacji danych
                byteList.Clear();
                byteList.Add(0x09);
                byteList.Add(0x01);

                await charac.WriteAsync(byteList.ToArray());

                byteList.Clear();
                byteList.Add(0x01);
                byteList.Add(0x01);

                await charac.WriteAsync(byteList.ToArray());
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
        }

        private void ParseRecords(List<byte[]> listOfRecords)
        {
            // lista na wszystkie dane
            var dataList = new List<byte>();

            // wyczyszczenie labela
            data.Devices = "";

            foreach (var t in listOfRecords)
            {
               foreach (var b in t)
               {
                    dataList.Add(b);
               }
            }

            DateTime recordDate = new DateTime(1970, 1, 1); // czas aktualnego pomiaru
            
            // zmienne na dane
            byte lsbT;
            byte msbT;
            double temperature;

            // każda ramka danych ma 8 bajtów więc iterujemy co 8
            for (int i=0; i<dataList.Count; i+=8)
            {
                if ((i+8) >= dataList.Count) break; 
                                   
                // jeżeli rekord zawiera czas to ustawiam recordDate
                if (IsTimeRecord(dataList[i], dataList[i + 5]))
                {
                    long timestamp = ((uint)dataList[i + 4] << 24) + ((uint)dataList[i + 3] << 16) + ((uint)dataList[i + 2] << 8) + ((uint)dataList[i + 1]);
                    recordDate = new DateTime(1970, 1, 1).AddHours(2).AddSeconds(timestamp);

                    if (dataList[i + 5] == 0) // rekord oznaczający początek danych nie zawiera żadnych danych
                        continue;

                    lsbT = dataList[i + 6];
                    msbT = dataList[i + 7];

                    temperature = CalculateTemp(lsbT, msbT);
                    data.Devices += recordDate.ToString() + " || " + temperature.ToString() + "\n";
                }
                else
                {
                    // jeżeli rekord nie zawiera czasu to na bajtach 1,2,3,4,6,7 są dane

                    if (dataList[i] == 176 && dataList[i + 6] == 255) // odfiltrowanie jakichś śmieci
                        continue;

                    // kolejne rekordy mają czas o minutę większy od poprzedniego
                    recordDate = recordDate.AddMinutes(1);

                    // pobieranie danych, konwersja na temperaturę i zapisanie do labela
                    lsbT = dataList[i + 1];
                    msbT = dataList[i + 2];

                    temperature = CalculateTemp(lsbT, msbT);
                    data.Devices += recordDate.ToString() + " || " + temperature.ToString() + "\n";

                    recordDate = recordDate.AddMinutes(1);

                    lsbT = dataList[i + 3];
                    msbT = dataList[i + 4];

                    temperature = CalculateTemp(lsbT, msbT);
                    data.Devices += recordDate.ToString() + " || " + temperature.ToString() + "\n";

                    recordDate = recordDate.AddMinutes(1);

                    lsbT = dataList[i + 6];
                    msbT = dataList[i + 7];

                    temperature = CalculateTemp(lsbT, msbT);
                    data.Devices += recordDate.ToString() + " || " + temperature.ToString() + "\n";
                }
            }
        }

        /// <summary>
        /// Sprawdza czy rekord jest rekordem zawierającym informacje o czasie
        /// </summary>
        /// <param name="b0"></param>
        /// <param name="b5"></param>
        /// <returns></returns>
        private bool IsTimeRecord(byte b0, byte b5)
        {
            return ((b0 >> 4) == 0b1010) && (b5 == 0 || b5 == 0x83); // z dokumentacji
        }

        /// <summary>
        /// Obliczenie temperatury z dokumentacji
        /// </summary>
        /// <param name="lsbT"></param>
        /// <param name="msbT"></param>
        /// <returns></returns>
        private double CalculateTemp(byte lsbT, byte msbT)
        {
            //double temperature = msbT * 0.0625 + 16 * (lsbT & 0x0F);

            //if ((lsbT & 0x10) != 0)
            //{
            //    temperature = temperature - 256;
            //}

            //if (temperature < -30) temperature = -30;
            //else if (temperature > 70) temperature = 70;

            //return temperature;

            var raw = ((msbT << 8) + lsbT);
            return (175.72 * raw * 4 / 65536) - 46.85;
        }
    }
}
