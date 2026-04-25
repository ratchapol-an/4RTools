namespace RagnarokAutomation.Core;

public static class CharacterNameMemoryResolver
{
    public static string TryReadCharacterName(ProcessMemoryReader memoryReader, int processId, int nameAddress, int moduleBase)
    {
        string value = memoryReader.ReadAsciiString(processId, nameAddress);
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        int pointed = memoryReader.TryReadPointer32(processId, nameAddress);
        if (pointed > 0)
        {
            value = memoryReader.ReadAsciiString(processId, pointed);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        if (moduleBase <= 0)
        {
            return string.Empty;
        }

        long relative = (long)moduleBase + nameAddress;
        if (relative is <= 0 or > int.MaxValue)
        {
            return string.Empty;
        }

        value = memoryReader.ReadAsciiString(processId, (int)relative);
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        pointed = memoryReader.TryReadPointer32(processId, (int)relative);
        if (pointed > 0)
        {
            value = memoryReader.ReadAsciiString(processId, pointed);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }
}
