using HVO;

namespace HVO.NinaClient.Models;

[NamedOneOf("HistoryItems", typeof(List<ImageHistoryItem>), "Count", typeof(int))]
public partial class ImageHistoryResponse;
