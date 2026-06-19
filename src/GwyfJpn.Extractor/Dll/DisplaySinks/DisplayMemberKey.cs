using System;
using System.Collections.Generic;
using System.Linq;
using dnlib.DotNet;

namespace GwyfJpn.Extractor;

/// <summary>
/// Stable dnlib keys used by the static display-flow passes.
/// Signature keys let separate catalog passes share evidence without metadata tokens.
/// </summary>
internal static class DisplayMemberKey
{
    public static string ForMethod(MethodDef method)
    {
        var declaringType = IlOpcodeHelpers.TypeFullName(method.DeclaringType);
        var parameters = string.Join(",", method.Parameters.Select(p => p.Type.FullName));
        return declaringType + "::" + IlOpcodeHelpers.MethodName(method) + "(" + parameters + ")";
    }

    public static string ForField(FieldDef field)
    {
        var declaringType = IlOpcodeHelpers.TypeFullName(field.DeclaringType);
        if (string.IsNullOrEmpty(declaringType))
        {
            declaringType = IlOpcodeHelpers.TypeName(field.DeclaringType);
        }

        return declaringType + "::" + IlOpcodeHelpers.FieldName(field);
    }
}
