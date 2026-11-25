namespace SEP.WebApiEtikit.Labeling
{
    public interface ILabelSvgService
    {
        string RenderZpl(string zpl);

        string RenderTpcl(string tpcl);
    }

    public sealed class LabelSvgService : ILabelSvgService
    {
        private readonly ZplLabelParser _zplParser = new();
        private readonly TpclLabelParser _tpclParser = new();
        private readonly SvgLabelRenderer _renderer = new();

        public string RenderZpl(string zpl)
        {
            var document = _zplParser.Parse(zpl);
            return _renderer.Render(document);
        }

        public string RenderTpcl(string tpcl)
        {
            var document = _tpclParser.Parse(tpcl);
            return _renderer.Render(document);
        }
    }
}
