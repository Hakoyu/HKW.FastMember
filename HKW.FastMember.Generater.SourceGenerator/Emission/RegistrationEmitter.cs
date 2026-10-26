using System.CodeDom.Compiler;
using System.Collections.Generic;
using HKW.SourceGeneratorUtils;
using Microsoft.CodeAnalysis;

namespace HKW.FastMember.Generater.SourceGenerator.Emission;

internal static class RegistrationEmitter
{
    internal static void WriteTo(IndentedTextWriter writer, string type, string identifier)
    {
        var registration = new ObjectGenerateInfo(
            "Registration_" + identifier,
            ObjectGenerateType.StaticClass
        )
        {
            Accessibility = Accessibility.Internal,
            AddDefaultAttributes = false,
            Attributes = [GeneratorHelper.GeneratedCodeAttribute],
        };
        registration.Members.Add(
            new MethodGenerateInfo(
                "void",
                "Register",
                $"global::HKW.FastMember.Generater.GeneratedAccessorRegistry<{type}>.Register(new Accessor_{identifier}());"
            )
            {
                Accessibility = Accessibility.Internal,
                IsStatic = true,
                AddDefaultAttributes = false,
                Attributes = new List<AttributeGenerateInfo>
                {
                    new("[global::System.Runtime.CompilerServices.ModuleInitializer]"),
                },
            }
        );
        writer.WriteInfo(registration);
    }
}
