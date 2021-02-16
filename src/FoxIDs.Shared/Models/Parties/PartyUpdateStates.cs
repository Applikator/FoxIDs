﻿using System.Runtime.Serialization;

namespace FoxIDs.Models
{
    public enum PartyUpdateStates
    {
        [EnumMember(Value = "Manual")]
        Manual = 10,
        [EnumMember(Value = "automatic")]
        Automatic = 20,
        [EnumMember(Value = "automatic_atopped")]
        AutomaticStopped = 30,
    }
}
