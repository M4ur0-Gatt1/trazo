namespace Trazo.Core.Documents;

/// <summary>Propiedades de un grupo de capas (LY-03). Un solo nivel: no hay grupos anidados.</summary>
public sealed record GroupProperties
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = "Grupo";
    public float Opacity { get; init; } = 1;
    public BlendMode Blend { get; init; } = BlendMode.Normal;
    public bool Visible { get; init; } = true;
    public bool Collapsed { get; init; }
}

/// <summary>
/// Reglas de pertenencia a grupos sobre la pila de capas (de abajo hacia arriba).
/// Invariante: las capas de un mismo grupo son contiguas.
/// </summary>
public static class GroupRules
{
    /// <summary>Comprueba que cada grupo ocupa un tramo contiguo de la pila.</summary>
    public static bool AreContiguous(IReadOnlyList<string?> groupIds)
    {
        var closed = new HashSet<string>();
        string? current = null;
        foreach (var g in groupIds)
        {
            if (g != current)
            {
                if (current is not null) closed.Add(current);
                if (g is not null && closed.Contains(g)) return false;
                current = g;
            }
        }
        return true;
    }

    /// <summary>
    /// Grupo que debe tener una capa recién movida a <paramref name="index"/> (la lista ya refleja el movimiento):
    /// si queda entre dos capas del mismo grupo, entra en él; si sigue junto a una capa de su grupo, lo conserva;
    /// en otro caso queda fuera de cualquier grupo.
    /// </summary>
    public static string? GroupAfterMove(IReadOnlyList<string?> groupIds, int index, string? ownGroup)
    {
        var below = index > 0 ? groupIds[index - 1] : null;
        var above = index < groupIds.Count - 1 ? groupIds[index + 1] : null;
        if (below is not null && below == above) return below;
        if (ownGroup is not null && (below == ownGroup || above == ownGroup)) return ownGroup;
        return null;
    }

    /// <summary>Grupo para una capa nueva insertada en <paramref name="index"/>: el de la capa de debajo (como en Procreate).</summary>
    public static string? GroupForInsert(IReadOnlyList<string?> groupIdsBeforeInsert, int index) =>
        index > 0 && index - 1 < groupIdsBeforeInsert.Count ? groupIdsBeforeInsert[index - 1] : null;

    /// <summary>Tramo [primero, último] de un grupo en la pila, o null si no tiene capas.</summary>
    public static (int First, int Last)? Range(IReadOnlyList<string?> groupIds, string groupId)
    {
        var first = -1;
        var last = -1;
        for (var i = 0; i < groupIds.Count; i++)
        {
            if (groupIds[i] != groupId) continue;
            if (first < 0) first = i;
            last = i;
        }
        return first < 0 ? null : (first, last);
    }
}
