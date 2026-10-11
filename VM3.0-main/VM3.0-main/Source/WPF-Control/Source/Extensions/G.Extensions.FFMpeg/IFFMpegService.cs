using FFMpegCore;

namespace G.Extensions.FFMpeg;

public interface IFFMpegService
{
    IMediaAnalysis GetMediaAnalysis(string url);
}