using System.Linq;
using System.Reflection;
using SSMP.Api.Command.Server;
using SSMP.Game.Server;
using SSMP.Game.Settings;
using SSMP.Api.Command;
using SSMP.Util;

namespace SSMP.Game.Command.Server;

/// <summary>
/// Command for managing server settings.
/// </summary>
internal class SettingsCommand : IServerCommand, ICommandWithDescription {
    /// <inheritdoc />
    public string Trigger => "/set";

    /// <inheritdoc />
    public string[] Aliases => [];

    /// <inheritdoc />
    public string Description => "Read or write a setting with the given name and optional value.";

    /// <inheritdoc />
    public bool AuthorizedOnly => true;

    /// <summary>
    /// The server manager instance.
    /// </summary>
    private readonly ServerManager _serverManager;

    /// <summary>
    /// The server settings.
    /// </summary>
    protected readonly ServerSettings ServerSettings;

    public SettingsCommand(ServerManager serverManager, ServerSettings serverSettings) {
        _serverManager = serverManager;
        ServerSettings = serverSettings;
    }

    /// <inheritdoc />
    public virtual void Execute(ICommandSender commandSender, string[] args) {
        if (args.Length < 2) {
            commandSender.SendMessage($"Usage: {Trigger} <name> [value]");
            return;
        }

        var settingName = args[1].ToLower().Replace("_", "").Replace("-", "");

        var propertyInfos = typeof(ServerSettings).GetProperties();

        PropertyInfo? settingProperty = null;
        foreach (var prop in propertyInfos) {
            // Check if the property name in lower case equals the argument
            if (prop.Name.ToLower().Equals(settingName)) {
                settingProperty = prop;
                break;
            }
            
            // Alternatively check for the alias attribute and all aliases
            var aliasAttribute = prop.GetCustomAttribute<SettingAliasAttribute>();
            if (aliasAttribute != null) {
                if (aliasAttribute.Aliases.Contains(settingName)) {
                    settingProperty = prop;
                    break;
                }
            }
        }

        if (settingProperty == null || !settingProperty.CanRead) {
            commandSender.SendMessage($"Could not find setting with name: {settingName}");
            return;
        }
        
        var propName = settingProperty.Name;

        if (args.Length < 3) {
            // The user only supplied the name of the setting, so we print its value
            var displayedValue = ObservableReflection.GetUnwrappedPropertyValue(settingProperty, ServerSettings);

            commandSender.SendMessage($"Setting '{propName}' currently has value: {displayedValue}");
            return;
        }

        var newValueString = args[2];

        var settingType = ObservableReflection.UnwrapType(settingProperty.PropertyType);

        if (!settingProperty.CanWrite && !ObservableReflection.IsObservableType(settingProperty.PropertyType)) {
            commandSender.SendMessage($"Could not change value of setting with name: {propName} (non-writable)");
            return;
        }

        object newValueObject;

        if (settingType == typeof(bool)) {
            if (!bool.TryParse(newValueString, out var newValueBool)) {
                commandSender.SendMessage("Please provide a boolean value (true/false) for this setting");
                return;
            }

            newValueObject = newValueBool;
        } else if (settingType == typeof(byte)) {
            if (!byte.TryParse(newValueString, out var newValueByte)) {
                commandSender.SendMessage("Please provide a byte value (>= 0 and <= 255) for this setting");
                return;
            }

            newValueObject = newValueByte;
        } else {
            commandSender.SendMessage(
                $"Could not change value of setting with name: {propName} (unhandled type)");
            return;
        }

        var existingValue = ObservableReflection.GetUnwrappedPropertyValue(settingProperty, ServerSettings);
        if (Equals(existingValue, newValueObject)) {
            commandSender.SendMessage($"Setting '{propName}' already has value: {newValueObject}");
            return;
        }

        if (!ObservableReflection.TrySetPropertyValue(settingProperty, ServerSettings, newValueObject)) {
            commandSender.SendMessage($"Could not change value of setting with name: {propName} (non-writable)");
            return;
        }

        commandSender.SendMessage($"Changed setting '{propName}' to: {newValueObject}");

        _serverManager.OnUpdateServerSettings();
    }
}
