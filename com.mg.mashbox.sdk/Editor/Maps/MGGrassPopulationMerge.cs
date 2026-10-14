namespace MashBoxSDK.MapTools
{
    // Pure migration rule, independently executable without Unity native APIs.
    internal static class MGGrassPopulationMerge
    {
        internal static void Add(int index, ushort value, byte id, ushort size, ushort[][] counts, byte[][] ids, ushort[][] sizes, ushort[][] health = null, ushort healthValue = 0x3C00)
        {
            if (value == 0) return;
            int target;
            if (counts[0][index] > 0 && ids[0][index] == id) target = 0;
            else if (counts[1][index] > 0 && ids[1][index] == id) target = 1;
            else target = value > counts[0][index] ? 0 : value > counts[1][index] ? 1 : -1;
            if (target < 0 || value <= counts[target][index]) return;
            if (target == 0 && ids[0][index] != id)
            {
                counts[1][index] = counts[0][index]; ids[1][index] = ids[0][index]; sizes[1][index] = sizes[0][index];
                if (health != null) health[1][index] = health[0][index];
            }
            counts[target][index] = value; ids[target][index] = id; sizes[target][index] = size;
            if (health != null) health[target][index] = healthValue;
            if (counts[1][index] > counts[0][index])
            {
                ushort count = counts[0][index]; counts[0][index] = counts[1][index]; counts[1][index] = count;
                byte slice = ids[0][index]; ids[0][index] = ids[1][index]; ids[1][index] = slice;
                if (health != null) { ushort h = health[0][index]; health[0][index] = health[1][index]; health[1][index] = h; }
                ushort scale = sizes[0][index]; sizes[0][index] = sizes[1][index]; sizes[1][index] = scale;
            }
        }
    }
}