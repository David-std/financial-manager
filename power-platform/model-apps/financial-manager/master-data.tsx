import React, { useEffect, useMemo, useState } from "react";
import {
  Badge,
  Button,
  Card,
  CardHeader,
  Input,
  Spinner,
  Text,
  makeStyles,
  tokens,
} from "@fluentui/react-components";
import {
  ArrowClockwiseRegular,
  BuildingRegular,
  FolderRegular,
  WalletRegular,
  OpenRegular,
} from "@fluentui/react-icons";
import type {
  GeneratedComponentProps,
  fmg_category,
  fmg_branch,
  fmg_cashaccount,
} from "./RuntimeTypes";

type Catalog = {
  loading: boolean;
  categories: fmg_category[];
  branches: fmg_branch[];
  accounts: fmg_cashaccount[];
  error: string;
  limited: boolean;
};
const cache = "__financialManagerCatalog2026";
const useStyles = makeStyles({
  root: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalL,
    padding: tokens.spacingHorizontalXXL,
    backgroundColor: tokens.colorNeutralBackground2,
    color: tokens.colorNeutralForeground1,
    minHeight: "100%",
    boxSizing: "border-box",
  },
  header: {
    display: "flex",
    justifyContent: "space-between",
    flexWrap: "wrap",
    alignItems: "center",
    gap: tokens.spacingHorizontalM,
  },
  heading: { fontSize: tokens.fontSizeHero700, fontWeight: tokens.fontWeightSemibold },
  info: { color: tokens.colorNeutralForeground3, fontSize: tokens.fontSizeBase300 },
  cards: {
    display: "grid",
    gridTemplateColumns: "repeat(auto-fit, minmax(180px,1fr))",
    gap: tokens.spacingHorizontalM,
  },
  big: { fontSize: tokens.fontSizeHero600, fontWeight: tokens.fontWeightSemibold },
  bar: { display: "flex", alignItems: "center", gap: tokens.spacingHorizontalS, flexWrap: "wrap" },
  tableBox: {
    overflowX: "auto",
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground1,
  },
  table: { width: "100%", minWidth: "620px", borderCollapse: "collapse" },
  cell: {
    padding: tokens.spacingHorizontalM,
    borderBottom: "1px solid " + tokens.colorNeutralStroke2,
    textAlign: "left",
  },
  th: { fontWeight: tokens.fontWeightSemibold, backgroundColor: tokens.colorNeutralBackground3 },
  error: { color: tokens.colorPaletteRedForeground1 },
  empty: { padding: tokens.spacingHorizontalXXL, color: tokens.colorNeutralForeground3 },
});
function openRecord(table: string, id: string): void {
  const xrm = (window as any).Xrm;
  if (xrm?.Navigation?.navigateTo)
    void xrm.Navigation.navigateTo({ pageType: "entityrecord", entityName: table, entityId: id });
}
const MasterDataPage = ({ dataApi }: GeneratedComponentProps) => {
  const css = useStyles();
  const [state, setState] = useState<Catalog>(
    () =>
      (window as any)[cache] ?? {
        loading: true,
        categories: [],
        branches: [],
        accounts: [],
        error: "",
        limited: false,
      },
  );
  const [version, setVersion] = useState(0);
  const [tab, setTab] = useState<"category" | "branch" | "account">("category");
  const [search, setSearch] = useState("");
  const ready = !!dataApi;
  useEffect(() => {
    if (!ready) return;
    let active = true;
    const global = window as any;
    if (version === 0 && global[cache]) {
      setState(global[cache]);
      return;
    }
    setState((s) => ({ ...s, loading: true, error: "" }));
    const key = cache + "_request";
    if (!global[key])
      global[key] = Promise.all([
        dataApi.queryTable("fmg_category", {
          select: [
            "fmg_categoryid",
            "fmg_name",
            "fmg_code",
            "fmg_acronym",
            "fmg_isgroup",
            "fmg_isactive",
            "fmg_parentcode",
          ],
          orderBy: "fmg_code asc",
          pageSize: 100,
        }),
        dataApi.queryTable("fmg_branch", {
          select: ["fmg_branchid", "fmg_name", "fmg_isactive"],
          pageSize: 100,
        }),
        dataApi.queryTable("fmg_cashaccount", {
          select: [
            "fmg_cashaccountid",
            "fmg_name",
            "fmg_type",
            "fmg_isactive",
            "_fmg_branch_value",
          ],
          pageSize: 100,
        }),
      ])
        .then(([c, b, a]) => {
          const res: Catalog = {
            loading: false,
            categories: c.rows,
            branches: b.rows,
            accounts: a.rows,
            error: "",
            limited: c.hasMoreRows || b.hasMoreRows || a.hasMoreRows,
          };
          global[cache] = res;
          return res;
        })
        .catch((error: unknown) => {
          const res: Catalog = {
            loading: false,
            categories: [],
            branches: [],
            accounts: [],
            limited: false,
            error: error instanceof Error ? error.message : "No se pudieron cargar los catálogos.",
          };
          global[cache] = res;
          return res;
        })
        .finally(() => {
          global[key] = null;
        });
    (global[key] as Promise<Catalog>).then((result) => {
      if (active) setState(result);
    });
    return () => {
      active = false;
    };
  }, [ready, version]);
  const show = useMemo(() => {
    const s = search.toLowerCase().trim();
    if (tab === "category")
      return state.categories.filter(
        (r) =>
          !s ||
          [r.fmg_name, r.fmg_code, r.fmg_acronym].some((x) =>
            String(x || "")
              .toLowerCase()
              .includes(s),
          ),
      );
    if (tab === "branch")
      return state.branches.filter((r) => !s || r.fmg_name?.toLowerCase().includes(s));
    return state.accounts.filter((r) => !s || r.fmg_name?.toLowerCase().includes(s));
  }, [tab, search, state]);
  function refresh() {
    (window as any)[cache] = null;
    (window as any)[cache + "_request"] = null;
    setVersion((x) => x + 1);
  }
  return (
    <main className={css.root} aria-label="Catálogos financieros">
      <header className={css.header}>
        <div>
          <Text as="h1" className={css.heading}>
            Datos maestros
          </Text>
          <p className={css.info}>Consulta de categorías de egreso, sedes y cuentas internas</p>
        </div>
        <Button icon={<ArrowClockwiseRegular />} onClick={refresh} disabled={state.loading}>
          Actualizar
        </Button>
      </header>
      <div className={css.cards}>
        <Card>
          <CardHeader
            image={<FolderRegular />}
            header={<Text weight="semibold">Categorías</Text>}
          />
          <Text className={css.big}>{state.categories.length}</Text>
        </Card>
        <Card>
          <CardHeader image={<BuildingRegular />} header={<Text weight="semibold">Sedes</Text>} />
          <Text className={css.big}>{state.branches.length}</Text>
        </Card>
        <Card>
          <CardHeader image={<WalletRegular />} header={<Text weight="semibold">Cuentas</Text>} />
          <Text className={css.big}>{state.accounts.length}</Text>
        </Card>
      </div>
      <Card>
        <Text weight="semibold">Consulta protegida</Text>
        <Text size={200}>
          La modificación de catálogos corresponde a administradores. Las categorías inactivas o de
          grupo no pueden utilizarse al confirmar gastos; la validación se realiza en el servidor.
        </Text>
      </Card>
      <section>
        <div className={css.bar} style={{ marginBottom: "12px" }}>
          <Button
            appearance={tab === "category" ? "primary" : "secondary"}
            onClick={() => {
              setTab("category");
              setSearch("");
            }}
          >
            Categorías
          </Button>
          <Button
            appearance={tab === "branch" ? "primary" : "secondary"}
            onClick={() => {
              setTab("branch");
              setSearch("");
            }}
          >
            Sedes
          </Button>
          <Button
            appearance={tab === "account" ? "primary" : "secondary"}
            onClick={() => {
              setTab("account");
              setSearch("");
            }}
          >
            Cuentas de origen
          </Button>
          <Input
            aria-label="Buscar en catálogo"
            placeholder="Buscar nombre o código"
            value={search}
            onChange={(_, v) => setSearch(v.value)}
          />
        </div>
        {state.loading && <Spinner label="Cargando datos maestros..." />}
        {state.error && (
          <Text role="alert" className={css.error}>
            {state.error}
          </Text>
        )}
        {!state.loading && !state.error && show.length === 0 && (
          <div className={css.empty}>No se encontraron elementos para estos filtros.</div>
        )}
        {!state.loading && !state.error && show.length > 0 && (
          <div className={css.tableBox}>
            <table className={css.table}>
              <thead>
                <tr>
                  {(tab === "category"
                    ? ["Código", "Categoría", "Clase", "Estado", "Acción"]
                    : tab === "branch"
                      ? ["Sede", "Estado", "Acción"]
                      : ["Cuenta", "Tipo", "Estado", "Acción"]
                  ).map((h) => (
                    <th className={css.cell + " " + css.th} key={h}>
                      {h}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {tab === "category" &&
                  state.categories
                    .filter((r) => show.includes(r))
                    .map((r) => (
                      <tr key={r.fmg_categoryid}>
                        <td className={css.cell}>{r.fmg_code}</td>
                        <td className={css.cell}>{r.fmg_name}</td>
                        <td className={css.cell}>
                          {r.fmg_isgroup === 1 ? "Grupo" : "Categoría de detalle"}
                        </td>
                        <td className={css.cell}>
                          <Badge appearance="outline">
                            {r.fmg_isactive === 1 ? "Activa" : "Inactiva"}
                          </Badge>
                        </td>
                        <td className={css.cell}>
                          <Button
                            icon={<OpenRegular />}
                            appearance="subtle"
                            onClick={() => openRecord("fmg_category", r.fmg_categoryid)}
                          >
                            Abrir
                          </Button>
                        </td>
                      </tr>
                    ))}
                {tab === "branch" &&
                  state.branches
                    .filter((r) => show.includes(r))
                    .map((r) => (
                      <tr key={r.fmg_branchid}>
                        <td className={css.cell}>{r.fmg_name}</td>
                        <td className={css.cell}>
                          <Badge appearance="outline">
                            {r.fmg_isactive === 1 ? "Activa" : "Inactiva"}
                          </Badge>
                        </td>
                        <td className={css.cell}>
                          <Button
                            icon={<OpenRegular />}
                            appearance="subtle"
                            onClick={() => openRecord("fmg_branch", r.fmg_branchid)}
                          >
                            Abrir
                          </Button>
                        </td>
                      </tr>
                    ))}
                {tab === "account" &&
                  state.accounts
                    .filter((r) => show.includes(r))
                    .map((r) => (
                      <tr key={r.fmg_cashaccountid}>
                        <td className={css.cell}>{r.fmg_name}</td>
                        <td className={css.cell}>
                          {(
                            {
                              797280000: "Caja",
                              797280001: "Banco",
                              797280002: "Billetera",
                              797280003: "Otro",
                            } as Record<number, string>
                          )[r.fmg_type] || "—"}
                        </td>
                        <td className={css.cell}>
                          <Badge appearance="outline">
                            {r.fmg_isactive === 1 ? "Activa" : "Inactiva"}
                          </Badge>
                        </td>
                        <td className={css.cell}>
                          <Button
                            icon={<OpenRegular />}
                            appearance="subtle"
                            onClick={() => openRecord("fmg_cashaccount", r.fmg_cashaccountid)}
                          >
                            Abrir
                          </Button>
                        </td>
                      </tr>
                    ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
      {state.limited && (
        <Text size={200}>
          La lista puede estar incompleta por paginación. Use las vistas nativas para consultar el
          resto.
        </Text>
      )}
    </main>
  );
};
export default MasterDataPage;
