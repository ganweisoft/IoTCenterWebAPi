using System.ComponentModel.DataAnnotations;

namespace IoTCenter.Data.Model
{
    public partial class GwprocCycleProcessTime
    {
        [Key]
        public int Id { get; set; }
        public int? TableId { get; set; }
        public int? Time { get; set; }
        public string ProcessOrder { get; set; }
    }
}
