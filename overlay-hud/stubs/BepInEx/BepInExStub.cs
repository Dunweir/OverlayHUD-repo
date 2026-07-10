using System;
using UnityEngine;

namespace BepInEx
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public sealed class BepInPluginAttribute : Attribute
    {
        public BepInPluginAttribute(string guid, string name, string version)
        {
            GUID = guid;
            Name = name;
            Version = version;
        }

        public string GUID { get; }
        public string Name { get; }
        public string Version { get; }
    }

    public class BaseUnityPlugin : MonoBehaviour
    {
        public Configuration.ConfigFile Config { get; } = new Configuration.ConfigFile();
        public Logging.ManualLogSource Logger { get; } = new Logging.ManualLogSource();
    }
}

namespace BepInEx.Configuration
{
    public sealed class ConfigFile
    {
        public ConfigEntry<T> Bind<T>(string section, string key, T defaultValue, string description)
        {
            return new ConfigEntry<T>(defaultValue);
        }
    }

    public sealed class ConfigEntry<T>
    {
        public ConfigEntry(T value)
        {
            Value = value;
        }

        public T Value { get; set; }
    }
}

namespace BepInEx.Logging
{
    public sealed class ManualLogSource
    {
        public void LogInfo(object data)
        {
        }
    }
}
