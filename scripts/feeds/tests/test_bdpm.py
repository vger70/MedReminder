import pytest

import bdpm
import common
from conftest import FIXTURES

SAMPLES = {
    bdpm.CIS: FIXTURES / "bdpm-cis-sample.txt",
    bdpm.CIP: FIXTURES / "bdpm-cip-sample.txt",
    bdpm.COMPO: FIXTURES / "bdpm-compo-sample.txt",
}


@pytest.fixture
def samples():
    return {name: path.read_bytes() for name, path in SAMPLES.items()}


@pytest.fixture
def low_floors(monkeypatch):
    for spec in bdpm.FILES.values():
        monkeypatch.setitem(spec, "min_rows", 50)


def test_find_links_reads_the_download_page():
    html = """
      <a href="/download/file/CIS_COMPO_bdpm.txt">Compositions</a>
      <a href="/download/file/CIS_bdpm.txt">Spécialités</a>
      <a href="https://mirror.example.org/files/CIS_CIP_bdpm.txt?v=2">Présentations</a>
      <a href="/download/file/CIS_GENER_bdpm.txt">Génériques</a>
    """
    links = bdpm.find_links(html, "https://base-donnees-publique.medicaments.gouv.fr/telechargement")

    assert links == {
        bdpm.CIS: "https://base-donnees-publique.medicaments.gouv.fr/download/file/CIS_bdpm.txt",
        bdpm.CIP: "https://mirror.example.org/files/CIS_CIP_bdpm.txt?v=2",
        bdpm.COMPO: "https://base-donnees-publique.medicaments.gouv.fr/download/file/CIS_COMPO_bdpm.txt",
    }


def test_find_links_falls_back_to_the_download_pattern():
    links = bdpm.find_links("<html><a href='/telechargement.php?fichier=CIS_bdpm.txt'>old</a></html>")

    assert links[bdpm.CIS] == "https://base-donnees-publique.medicaments.gouv.fr/download/file/CIS_bdpm.txt"
    assert links[bdpm.COMPO].endswith("/download/file/CIS_COMPO_bdpm.txt")


def test_validate_counts_the_rows_of_the_samples(samples, low_floors):
    assert bdpm.validate(samples, {}) == {bdpm.CIS: 102, bdpm.CIP: 129, bdpm.COMPO: 224}


def test_validate_applies_the_absolute_floors(samples):
    with pytest.raises(common.FeedError, match="at least 12000"):
        bdpm.validate(samples, {})


def test_validate_applies_ninety_percent_of_the_previous_run(samples, low_floors):
    with pytest.raises(common.FeedError, match="CIS_COMPO_bdpm.txt: 224 rows, at least 270"):
        bdpm.validate(samples, {bdpm.COMPO: 300})


def test_validate_requires_every_file(samples, low_floors):
    del samples[bdpm.CIP]
    with pytest.raises(common.FeedError, match="missing"):
        bdpm.validate(samples, {})


def test_an_html_page_is_rejected(samples, low_floors):
    samples[bdpm.CIS] = b"<!DOCTYPE html><html><body>Maintenance</body></html>"
    with pytest.raises(common.FeedError, match="HTML"):
        bdpm.validate(samples, {})


def test_a_line_with_another_column_count_is_rejected(samples, low_floors):
    samples[bdpm.COMPO] += b"60025403\tcomprim\xe9\t02202\n"
    with pytest.raises(common.FeedError, match="3 columns, 8 expected"):
        bdpm.validate(samples, {})


def test_the_first_cis_row_must_carry_an_authorisation_status(samples, low_floors):
    lines = samples[bdpm.CIS].split(b"\r\n")
    columns = lines[0].split(b"\t")
    columns[4] = b"Archiv\xe9e"
    lines[0] = b"\t".join(columns)
    samples[bdpm.CIS] = b"\r\n".join(lines)

    with pytest.raises(common.FeedError, match="Autorisation"):
        bdpm.validate(samples, {})


def test_cis_must_decode_as_cp1252(samples, low_floors):
    # 0x81 is undefined in Windows-1252.
    samples[bdpm.CIS] = samples[bdpm.CIS].replace(b"\xe9", b"\x81", 1)
    with pytest.raises(common.FeedError, match="cp1252"):
        bdpm.validate(samples, {})


def test_cip_accepts_utf8(samples, low_floors):
    samples[bdpm.CIP] = samples[bdpm.CIP].decode("cp1252").encode("utf-8")
    assert bdpm.validate(samples, {})[bdpm.CIP] == 129


def test_blank_lines_are_not_rows(samples, low_floors):
    samples[bdpm.CIS] += b"\r\n\r\n"
    assert bdpm.validate(samples, {})[bdpm.CIS] == 102
