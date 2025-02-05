﻿using System.Runtime.Serialization;

namespace FoxIDs.Models
{
    public enum UsageLogTokenTypes
    {
        [EnumMember(Value = "refresh_token")]
        RefreshToken = 10,
        [EnumMember(Value = "client_credentials")]
        ClientCredentials = 20,
        [EnumMember(Value = "delegation")]
        Delegation = 30
    }
}
