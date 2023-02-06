using System;
using System.Collections.Generic;
using Bencodex.Types;
using Libplanet;

namespace Nekoyume.Action
{
    public interface IRaidV2
    {
        Address AvatarAddress { get; }
        IEnumerable<Guid> EquipmentIds { get; }
        IEnumerable<Guid> CostumeIds { get; }
        IEnumerable<Guid> FoodIds { get; }
        IEnumerable<IValue> RuneSlotInfos { get; }
        bool PayNcg { get; }
    }
}
