using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

using Autodesk.AutoCAD.DatabaseServices;

namespace AutomacoesCivil3D
{
    // Modelo de persistência para o histórico do comando SOL_VAZAO_INCENDIO.
    // Salvo num JSON ao lado do .dwg com nome "<dwg>.solidos-fire.json".
    // Permite o comando de undo restaurar o estado original (CTop anterior + bacias).
    public sealed class SolidosVazaoIncendioEstado
    {
        public const int CurrentVersion = 1;

        [JsonPropertyName("version")]
        public int Version { get; set; } = CurrentVersion;

        [JsonPropertyName("dwg")]
        public string DwgPath { get; set; } = "";

        [JsonPropertyName("operacoes")]
        public List<OperacaoRegistrada> Operacoes { get; set; } = new List<OperacaoRegistrada>();

        public sealed class OperacaoRegistrada
        {
            [JsonPropertyName("timestamp_primeira")]
            public string TimestampPrimeira { get; set; }

            [JsonPropertyName("timestamp_ultima")]
            public string TimestampUltima { get; set; }

            [JsonPropertyName("device_handle")]
            public string DeviceHandle { get; set; }

            [JsonPropertyName("device_name")]
            public string DeviceName { get; set; }

            [JsonPropertyName("ctop_anterior_m3s")]
            public double CTopAnteriorM3s { get; set; }

            [JsonPropertyName("ctop_aplicado_m3s")]
            public double CTopAplicadoM3s { get; set; }

            [JsonPropertyName("coef_l_min_m2")]
            public double CoefLMinM2 { get; set; }

            [JsonPropertyName("area_usada_m2")]
            public double AreaUsadaM2 { get; set; }

            // Handles (string hexadecimal) das bacias desconectadas na PRIMEIRA aplicação.
            // Preservado entre aplicações repetidas pra que o undo sempre reconecte tudo.
            [JsonPropertyName("bacias_desconectadas")]
            public List<string> BaciasDesconectadas { get; set; } = new List<string>();
        }

        // Caminho do arquivo de estado para um DWG (mesma pasta, mesmo nome base + ".solidos-fire.json").
        // Retorna null se o DWG ainda não foi salvo em disco (não tem path absoluto).
        public static string ResolverCaminhoEstado(string dwgFullPath)
        {
            if (string.IsNullOrWhiteSpace(dwgFullPath)) return null;
            if (!Path.IsPathRooted(dwgFullPath)) return null;
            string folder = Path.GetDirectoryName(dwgFullPath);
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return null;
            string fileName = Path.GetFileNameWithoutExtension(dwgFullPath) + ".solidos-fire.json";
            return Path.Combine(folder, fileName);
        }

        public static SolidosVazaoIncendioEstado Carregar(string estadoPath)
        {
            if (string.IsNullOrEmpty(estadoPath) || !File.Exists(estadoPath))
            {
                return new SolidosVazaoIncendioEstado { DwgPath = "" };
            }

            try
            {
                string json = File.ReadAllText(estadoPath);
                JsonSerializerOptions opt = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                SolidosVazaoIncendioEstado estado = JsonSerializer.Deserialize<SolidosVazaoIncendioEstado>(json, opt);
                if (estado == null)
                {
                    return new SolidosVazaoIncendioEstado();
                }
                if (estado.Operacoes == null) estado.Operacoes = new List<OperacaoRegistrada>();
                return estado;
            }
            catch
            {
                // Arquivo corrompido: ignora e retorna novo (preservar o antigo seria pior — undo iria reconectar coisa errada).
                return new SolidosVazaoIncendioEstado();
            }
        }

        public void Salvar(string estadoPath)
        {
            if (string.IsNullOrEmpty(estadoPath)) return;
            JsonSerializerOptions opt = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            string json = JsonSerializer.Serialize(this, opt);
            File.WriteAllText(estadoPath, json);
        }

        // Registra/atualiza uma operação no estado.
        // Política: se já existe operação para o mesmo device handle, PRESERVA o estado original
        // (ctop_anterior e bacias_desconectadas), atualizando apenas o que mudou.
        public void RegistrarOuAtualizar(OperacaoRegistrada nova)
        {
            if (nova == null || string.IsNullOrEmpty(nova.DeviceHandle)) return;

            OperacaoRegistrada existente = Operacoes.FirstOrDefault(
                op => string.Equals(op.DeviceHandle, nova.DeviceHandle, StringComparison.OrdinalIgnoreCase));

            if (existente == null)
            {
                if (string.IsNullOrEmpty(nova.TimestampPrimeira)) nova.TimestampPrimeira = DateTime.Now.ToString("o");
                nova.TimestampUltima = nova.TimestampPrimeira;
                Operacoes.Add(nova);
                return;
            }

            // Já existia: mantém estado original e atualiza só o que mudou.
            existente.TimestampUltima = DateTime.Now.ToString("o");
            existente.DeviceName = nova.DeviceName ?? existente.DeviceName;
            existente.CTopAplicadoM3s = nova.CTopAplicadoM3s;
            existente.CoefLMinM2 = nova.CoefLMinM2;
            existente.AreaUsadaM2 = nova.AreaUsadaM2;
            // NÃO sobrescreve: CTopAnteriorM3s, BaciasDesconectadas, TimestampPrimeira
        }

        public void RemoverPorHandle(string deviceHandle)
        {
            Operacoes.RemoveAll(op =>
                string.Equals(op.DeviceHandle, deviceHandle, StringComparison.OrdinalIgnoreCase));
        }

        public static string ObjectIdToHandle(ObjectId id)
        {
            if (id.IsNull) return "";
            return id.Handle.Value.ToString("X");
        }

        public static bool TryHandleToObjectId(Database db, string handleHex, out ObjectId id)
        {
            id = ObjectId.Null;
            if (db == null || string.IsNullOrWhiteSpace(handleHex)) return false;
            try
            {
                long val = Convert.ToInt64(handleHex, 16);
                Handle h = new Handle(val);
                return db.TryGetObjectId(h, out id);
            }
            catch
            {
                return false;
            }
        }
    }
}
