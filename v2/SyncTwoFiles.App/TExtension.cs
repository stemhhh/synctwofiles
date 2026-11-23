using System;
using System.Windows.Markup;
using SyncTwoFiles.Core;

namespace SyncTwoFiles.App;

// {l:T clave} en XAML: texto traducido al idioma activo.
[MarkupExtensionReturnType(typeof(string))]
public sealed class TExtension : MarkupExtension
{
    public TExtension() { }
    public TExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider) => L.T(Key);
}
