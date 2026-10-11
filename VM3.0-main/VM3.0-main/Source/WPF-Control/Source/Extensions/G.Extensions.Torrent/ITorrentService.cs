using G.Iocable;

namespace G.Extensions.Torrent;

public interface ITorrentService
{
    TorrentInfo CreateInfo(string torrentFile);
}

public class IocTorrentService : Ioc<ITorrentService>
{

}