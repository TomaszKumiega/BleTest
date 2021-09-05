using Plugin.BLE.Abstractions.Contracts;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace App1
{
    public class Data : INotifyPropertyChanged
    {
        private string _devices;
        public string Devices 
        {
            get => _devices;  
            set
            {
                _devices = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Devices"));
            }
        }

        public List<IDevice> DevicesObjects { get; set; }

        public Data()
        {
            DevicesObjects = new List<IDevice>();
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
