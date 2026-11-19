using System.Runtime.InteropServices;

namespace FieldKit.Services;

internal sealed record AntivirusProduct(string Name, int State);

internal static class SecurityProviders
{
    // IWSCProductList/IWscProduct, iwscapi.h. Use the documented vtables:
    // some machines register the COM server without an automation type library.
    [ComImport, Guid("722A338C-6E8E-4E72-AC27-1417FB0C81C2"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface IProductList
    {
        void Initialize(uint provider);
        int GetCount();
        [return: MarshalAs(UnmanagedType.Interface)] IProduct GetItem(uint index);
    }

    [ComImport, Guid("8C38232E-3A45-4A27-92B0-1A16A975F669"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface IProduct
    {
        [return: MarshalAs(UnmanagedType.BStr)] string GetName();
        int GetState();
    }

    internal static IReadOnlyList<AntivirusProduct> Read()
    {
        var cls = Type.GetTypeFromCLSID(new Guid("17072F7B-9ABE-4A74-A261-1EB76B55107A"), true)!;
        var list = (IProductList)Activator.CreateInstance(cls)!;
        try
        {
            list.Initialize(4); // WSC_SECURITY_PROVIDER_ANTIVIRUS
            var products = new List<AntivirusProduct>();
            var count = list.GetCount();
            for (uint i = 0; i < count; i++)
            {
                var item = list.GetItem(i);
                try { products.Add(new(item.GetName(), item.GetState())); }
                finally { Marshal.ReleaseComObject(item); }
            }
            return products;
        }
        finally { Marshal.ReleaseComObject(list); }
    }
}
