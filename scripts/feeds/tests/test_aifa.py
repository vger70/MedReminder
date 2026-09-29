import shutil

import pytest

import aifa
import common
from conftest import FIXTURES

CONFEZIONI_SAMPLE = FIXTURES / "aifa-confezioni-sample.csv"
PA_SAMPLE = FIXTURES / "aifa-pa-sample.csv"


@pytest.fixture
def downloaded(tmp_path):
    paths = {
        aifa.CONFEZIONI: tmp_path / aifa.CONFEZIONI,
        aifa.PA: tmp_path / aifa.PA,
    }
    shutil.copy(CONFEZIONI_SAMPLE, paths[aifa.CONFEZIONI])
    shutil.copy(PA_SAMPLE, paths[aifa.PA])
    return paths


@pytest.fixture
def low_floors(monkeypatch):
    monkeypatch.setitem(aifa.MIN_DATA_ROWS, aifa.CONFEZIONI, 10)
    monkeypatch.setitem(aifa.MIN_DATA_ROWS, aifa.PA, 10)


def test_find_links_keeps_the_two_csv_files_once():
    html = """
      <a href="/documents/20142/825643/confezioni_fornitura.csv">Confezioni</a>
      <a href="https://www.aifa.gov.it/documents/20142/825643/PA_confezioni.csv">PA</a>
      <a href="/documents/20142/825643/confezioni_fornitura.csv">again</a>
      <a href="/documents/20142/825643/atc.csv">ATC</a>
    """

    assert aifa.find_links(html) == [
        "https://www.aifa.gov.it/documents/20142/825643/PA_confezioni.csv",
        "https://www.aifa.gov.it/documents/20142/825643/confezioni_fornitura.csv",
    ]


def test_validate_counts_the_non_empty_lines_of_the_samples(downloaded, low_floors):
    counts = aifa.validate(downloaded, {})

    assert set(counts) == {aifa.CONFEZIONI, aifa.PA}
    assert counts[aifa.PA] == 252
    assert counts[aifa.CONFEZIONI] > 0


def test_validate_applies_the_absolute_floors(downloaded):
    with pytest.raises(common.FeedError, match="at least 100000"):
        aifa.validate(downloaded, {})


def test_validate_applies_ninety_percent_of_the_previous_run(downloaded, low_floors):
    with pytest.raises(common.FeedError, match="PA_confezioni.csv: 252 rows, at least 270"):
        aifa.validate(downloaded, {aifa.PA: 300})


def test_validate_requires_both_files(downloaded, low_floors):
    del downloaded[aifa.PA]
    with pytest.raises(common.FeedError, match="PA_confezioni.csv was not downloaded"):
        aifa.validate(downloaded, {})


def test_validate_matches_file_names_case_insensitively(downloaded, low_floors):
    renamed = {name.upper(): path for name, path in downloaded.items()}
    assert set(aifa.validate(renamed, {})) == {aifa.CONFEZIONI, aifa.PA}


def test_validate_rejects_a_missing_column(downloaded, low_floors):
    path = downloaded[aifa.PA]
    path.write_text("CODICE_AIC;QUANTITA\n" + "1;2\n" * 20, encoding="latin-1")

    with pytest.raises(common.FeedError, match="PRINCIPIO_ATTIVO"):
        aifa.validate(downloaded, {})
