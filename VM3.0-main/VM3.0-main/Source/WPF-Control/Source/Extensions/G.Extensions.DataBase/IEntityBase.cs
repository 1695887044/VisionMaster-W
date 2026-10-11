namespace G.Extensions.DataBase;

public interface IEntityBase<TPrimaryKey>
{
    TPrimaryKey ID { get; set; }
}