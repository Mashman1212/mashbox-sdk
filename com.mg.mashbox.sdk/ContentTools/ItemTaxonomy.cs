using System;

namespace MashBoxSDK.ContentTools
{
    public static class EnumExtensions
    {
        public static string ToDisplayName(this ItemType type)
        {
            return type.ToString().Replace("_", " ");
        }
    }
    
    [Serializable]
    public enum SuperType
    {
        Human,
        Drone,
        Marker,
        BMX,
        Scooter,
        MTB,
        Skateboard
    }

    [Serializable]
    public enum ItemType
    {
        // BMX
        Frame,
        Bars,
        Forks,
        Headset,
        BB,
        CrankArm,
        Seat_Post,
        Seat_Clamp,
        Seat,
        Pedal,
        Stem_Bolt,
        Stem,
        Stem_Cap,
        Sprocket,
        Grip,
        Mag,

        Brake_Rotor,
        Brake_Caliper,
        Brake_Lever,
        Shifter,
        Derailleur,
            
        // Scooter
        Deck,
        Clamp,

        Valve_Cap,
        
        Bust,
        Body,
        Shirt,
        Pants,
        Shoes,
        Gloves,
        Socks,
        Hat,
        Hair,
        Eyewear,
        Base,
        
        Griptape,
        Peg,
        Wheel,
        Urethane,
        Bar_End,
        Nipples,
        Spokes,
        Hub_Guard,
        Rim,
        Front_Hub,
        Rear_Hub,
        Tire,
        Chain,
        Accessory,
        Full_Skin

    }
}
