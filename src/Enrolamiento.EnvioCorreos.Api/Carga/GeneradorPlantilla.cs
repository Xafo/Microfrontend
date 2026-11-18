using ClosedXML.Excel;
using Enrolamiento.Enlaces;
using Microsoft.Extensions.Options;

namespace Enrolamiento.EnvioCorreos.Api.Carga;

/// <summary>
/// Entrega la plantilla de Excel con una leyenda, a la derecha de los datos, con los tipos de formulario
/// aceptados según la configuración (Enlaces:TiposFormularioPermitidos). Agregar un tipo en appsettings
/// lo agrega también a la leyenda.
/// </summary>
public sealed class GeneradorPlantilla(IOptions<EnlacesOptions> enlaces, IOptions<CargaOptions> carga)
{
    public static readonly string Ruta = Path.Combine(AppContext.BaseDirectory, "Plantillas", "Enrolamiento_EnvioCorreos.xlsx");

    public byte[] Generar()
    {
        using var libro = new XLWorkbook(Ruta);
        var hoja = libro.Worksheets.TryGetWorksheet(carga.Value.NombreHoja, out var h) ? h : libro.Worksheet(1);

        // Leyenda dos columnas a la derecha de la última columna de datos.
        var col = hoja.Row(1).LastCellUsed().Address.ColumnNumber + 2;
        var titulo = hoja.Cell(1, col);
        titulo.Value = "Tipos de formulario aceptados";
        titulo.Style.Font.Bold = true;
        titulo.Style.Font.FontColor = XLColor.White;
        titulo.Style.Fill.BackgroundColor = XLColor.FromHtml("#003DA5");

        var nota = hoja.Cell(2, col);
        nota.Value = "Escriba en «Tipo de Formulario» el nombre completo, tal como aparece aquí:";
        nota.Style.Font.Italic = true;
        nota.Style.Font.FontColor = XLColor.FromHtml("#5B6676");

        var fila = 3;
        foreach (var tipo in enlaces.Value.TiposFormularioPermitidos)
        {
            var c = hoja.Cell(fila++, col);
            c.SetValue(tipo);
            c.Style.Fill.BackgroundColor = XLColor.FromHtml("#E8EEF9");
            c.Style.Font.FontColor = XLColor.FromHtml("#002A73");
            c.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            c.Style.Border.OutsideBorderColor = XLColor.FromHtml("#D9DEE7");
        }
        hoja.Column(col).Width = 62;

        using var ms = new MemoryStream();
        libro.SaveAs(ms);
        return ms.ToArray();
    }
}
