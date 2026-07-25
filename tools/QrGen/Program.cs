using QRCoder;

// Generates scannable SVG QR codes for the project's crypto donation addresses.
// Usage: dotnet run --project tools/QrGen -- <output-dir>   (default: docs/assets)

var outDir = args.Length > 0 ? args[0] : Path.Combine("docs", "assets");
Directory.CreateDirectory(outDir);

var wallets = new (string Coin, string Address)[]
{
    ("xrp", "r4FaiziXJCbh2asirLkRpkGjLB47uHWNpE"),
    ("xlm", "GCTCVG44ZOJRYJXTFF7BA23ATPC47H3YOX22WB7X2AKBL3AZ35NR5KJY"),
};

using var generator = new QRCodeGenerator();
foreach (var (coin, address) in wallets)
{
    using var data = generator.CreateQrCode(address, QRCodeGenerator.ECCLevel.M);
    var svg = new SvgQRCode(data).GetGraphic(4);
    var path = Path.Combine(outDir, $"{coin}-qr.svg");
    File.WriteAllText(path, svg);
    Console.WriteLine($"Wrote {path} for {address}");
}
