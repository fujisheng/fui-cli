"""透明流程的像素级与 CLI 回归；临时合成图片仅作诊断夹具，不作美术资产。"""

import json
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest

from PIL import Image


SCRIPTS = Path(__file__).resolve().parents[1]


class AlphaPipelineTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="fui-alpha-tests-")
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        for folder in ("Assets", "Packages", "ProjectSettings", "FUI-CLI/Test"):
            (self.root / folder).mkdir(parents=True)
        self.work = self.root / "FUI-CLI/Test"
        self.source = self.work / "source.png"
        self.output = self.work / "output.png"
        self.native().save(self.source)

    def native(self):
        image = Image.new("RGBA", (8, 6), (0, 0, 0, 0))
        # 主体刻意使用品红色，证明 native 路线不会把合法主体颜色扣掉。
        for x in range(2, 6):
            for y in range(1, 5):
                image.putpixel((x, y), (255, 0, 255, 128 if x == 2 else 255))
        return image

    def single(self, *args, source=None):
        return subprocess.run([
            sys.executable, "-B", str(SCRIPTS / "postprocess_asset.py"),
            "--source", str(source or self.source), "--output", str(self.output), *args,
        ], capture_output=True, text=True)

    def batch(self, assets, *args):
        node = shutil.which("node")
        self.assertIsNotNone(node, "Node.js is required for CLI integration tests")
        manifest = self.work / "asset-manifest.json"
        manifest.write_text(json.dumps({"assets": assets}), encoding="utf-8")
        return subprocess.run([
            node, str(SCRIPTS / "process-ui-assets.mjs"),
            "--manifest", "FUI-CLI/Test/asset-manifest.json", "--python", sys.executable,
            *args,
        ], cwd=self.root, capture_output=True, text=True)

    def asset(self, **fields):
        return {"id": "test", "repairedAsset": "source.png", "file": "output.png",
                "transparent": True, **fields}

    def report(self, result):
        self.assertEqual(result.returncode, 0, result.stderr + result.stdout)
        return json.loads(result.stdout)

    def assert_pixels_equal(self, expected, path):
        with Image.open(path) as actual:
            self.assertEqual(actual.size, expected.size)
            self.assertEqual(actual.convert("RGBA").tobytes(), expected.convert("RGBA").tobytes())

    def test_native_keep_preserves_canvas_and_semitransparent_edges(self):
        data = self.report(self.single("--alpha-required"))
        self.assert_pixels_equal(self.native(), self.output)
        self.assertEqual(data["sourceMode"], "RGBA")
        self.assertEqual(data["sourceAlpha"], data["outputAlpha"])
        self.assertEqual(data["sourceAlpha"]["semiTransparentPixels"], 4)
        self.assertNotIn("chromaResidue", data)

    def test_rgb_and_fully_opaque_rgba_rejected_before_contain(self):
        for mode in ("RGB", "RGBA"):
            with self.subTest(mode=mode):
                Image.new(mode, (8, 6), "white").save(self.source)
                self.output.write_bytes(b"existing output")
                result = self.single("--alpha-required", "--width", "16", "--height", "16", "--fit", "contain")
                self.assertEqual(result.returncode, 1)
                self.assertIn("source: transparency required", result.stdout)
                self.assertEqual(self.output.read_bytes(), b"existing output")

    def test_fully_transparent_source_rejected_even_without_requirement(self):
        Image.new("RGBA", (8, 6), (1, 2, 3, 0)).save(self.source)
        result = self.single()
        self.assertEqual(result.returncode, 1)
        self.assertIn("fully transparent", result.stdout)
        self.assertFalse(self.output.exists())

    def test_all_semitransparent_effect_is_valid(self):
        Image.new("RGBA", (8, 6), (1, 2, 3, 128)).save(self.source)
        data = self.report(self.single("--alpha-required"))
        self.assertEqual(data["outputAlpha"]["semiTransparentPixels"], 48)

    def test_palette_transparency_is_valid(self):
        image = Image.new("P", (8, 6), 0)
        image.putpalette([0, 0, 0, 255, 100, 20] + [0] * 762)
        image.putpixel((3, 3), 1)
        image.save(self.source, transparency=0)
        self.assertEqual(self.report(self.single("--alpha-required"))["sourceMode"], "P")

    def test_opaque_background_is_valid(self):
        Image.new("RGB", (8, 6), "blue").save(self.source)
        data = self.report(self.single("--alpha-source-kind", "opaque"))
        self.assertEqual(data["outputAlpha"]["min"], 255)

    def test_trim_requires_explicit_request_and_preserves_partial_alpha(self):
        data = self.report(self.single("--alpha-required", "--alpha-mode", "trim", "--padding", "2"))
        self.assertEqual(data["outputSize"], {"width": 8, "height": 8})
        with Image.open(self.output) as result:
            self.assertEqual(result.crop((2, 2, 6, 6)).tobytes(), self.native().crop((2, 1, 6, 5)).tobytes())

    def test_trim_cannot_silently_remove_required_transparency(self):
        image = Image.new("RGBA", (8, 6), (0, 0, 0, 0))
        image.paste((0, 255, 0, 255), (2, 1, 6, 5))
        image.save(self.source)
        result = self.single("--alpha-required", "--alpha-mode", "trim")
        self.assertEqual(result.returncode, 1)
        self.assertIn("output: transparency required", result.stdout)

    def test_resize_still_runs_with_keep(self):
        data = self.report(self.single("--alpha-required", "--width", "16", "--height", "12"))
        self.assertEqual(data["outputSize"], {"width": 16, "height": 12})

    def test_fit_none_preserves_canvas_even_with_requested_size(self):
        self.report(self.single("--alpha-required", "--width", "16", "--height", "12", "--fit", "none"))
        self.assert_pixels_equal(self.native(), self.output)

    def test_native_rejects_destructive_or_chroma_options(self):
        for option in (("--despill",), ("--edge-contract", "1"), ("--edge-feather", "1"),
                       ("--chroma-key-color", "#ff00ff"), ("--max-chroma-residue-ratio", "0"),
                       ("--chroma-auto-key", "border"), ("--padding", "2")):
            with self.subTest(option=option):
                self.assertNotEqual(self.single("--alpha-required", *option).returncode, 0)
        self.assertFalse(self.output.exists())

    def test_source_kind_conflicts_are_rejected(self):
        for args in (("--alpha-source-kind", "native", "--alpha-mode", "chroma"),
                     ("--alpha-source-kind", "chroma", "--alpha-mode", "keep"),
                     ("--alpha-source-kind", "opaque", "--alpha-required")):
            with self.subTest(args=args):
                self.assertNotEqual(self.single(*args).returncode, 0)

    def test_transparent_output_rejects_jpeg(self):
        self.output = self.work / "output.jpg"
        self.assertNotEqual(self.single("--alpha-required").returncode, 0)
        self.assertFalse(self.output.exists())

    def test_native_webp_input_is_valid(self):
        source = self.work / "source.webp"
        self.native().save(source, lossless=True)
        self.report(self.single("--alpha-required", source=source))
        self.assert_pixels_equal(self.native(), self.output)

    def chroma_source(self):
        image = Image.new("RGB", (8, 6), (255, 0, 255))
        image.paste((0, 255, 0), (2, 1, 6, 5))
        image.save(self.source)

    def test_explicit_chroma_preserves_canvas_and_clears_background(self):
        self.chroma_source()
        data = self.report(self.single("--alpha-required", "--alpha-mode", "chroma-soft",
                                       "--chroma-key-color", "#ff00ff", "--max-chroma-residue-ratio", "0"))
        self.assertEqual(data["outputSize"], {"width": 8, "height": 6})
        self.assertEqual(data["outputAlpha"]["transparentPixels"], 32)
        self.assertTrue(data["chromaResidue"]["ok"])
        with Image.open(self.output) as image:
            self.assertEqual(image.getpixel((2, 1)), (0, 255, 0, 255))

    def test_chroma_sampling_requires_explicit_opt_in(self):
        self.chroma_source()
        self.assertNotEqual(self.single("--alpha-mode", "chroma").returncode, 0)
        self.report(self.single("--alpha-required", "--alpha-mode", "chroma", "--chroma-auto-key", "corners"))

    def test_chroma_all_background_is_rejected(self):
        Image.new("RGB", (8, 6), (255, 0, 255)).save(self.source)
        result = self.single("--alpha-required", "--alpha-mode", "chroma", "--chroma-key-color", "#ff00ff")
        self.assertEqual(result.returncode, 1)
        self.assertIn("output: image is fully transparent", result.stdout)

    def test_single_dry_run_and_failure_report(self):
        self.report(self.single("--alpha-required", "--dry-run"))
        self.assertFalse(self.output.exists())
        Image.new("RGB", (8, 6), "white").save(self.source)
        report = self.work / "failure.json"
        self.assertEqual(self.single("--alpha-required", "--report", str(report)).returncode, 1)
        self.assertFalse(json.loads(report.read_text())["ok"])

    def test_batch_default_keeps_native_alpha_and_generation_log(self):
        log = self.work / "asset-generation-log.json"
        log.write_text('{"prompt":"preserve me"}')
        data = self.report(self.batch([self.asset(imageType="sliced", spriteBorder=[2, 1, 2, 1])]))
        self.assert_pixels_equal(self.native(), self.output)
        self.assertEqual(data["results"][0]["alphaMode"], "keep")
        self.assertEqual(log.read_text(), '{"prompt":"preserve me"}')
        self.assertTrue((self.work / "asset-processing-report.json").exists())
        # 默认 keep 不裁边或缩放，九宫格使用的像素坐标系保持不变。
        manifest = json.loads((self.work / "asset-manifest.json").read_text())
        self.assertEqual(manifest["assets"][0]["spriteBorder"], [2, 1, 2, 1])

    def test_batch_does_not_infer_chroma_from_directory_name(self):
        folder = self.work / "ai_chroma_sources"
        folder.mkdir()
        self.native().save(folder / "native.png")
        self.report(self.batch([self.asset(repairedAsset="ai_chroma_sources/native.png")]))
        self.assert_pixels_equal(self.native(), self.output)

    def test_batch_declared_missing_source_does_not_use_old_output(self):
        self.native().save(self.output)
        data = json.loads(self.batch([self.asset(alphaSource="missing.png", path="output.png")]).stdout)
        self.assertFalse(data["ok"])
        self.assertEqual(data["results"][0]["error"], "source_not_found")
        self.assert_pixels_equal(self.native(), self.output)

    def test_batch_dry_run_writes_neither_images_nor_reports(self):
        self.report(self.batch([self.asset()], "--dry-run", "--report", "FUI-CLI/Test/dry.json"))
        self.assertFalse(self.output.exists())
        self.assertFalse((self.work / "dry.json").exists())
        self.assertFalse((self.work / "asset-processing-report.json").exists())

    def test_batch_chroma_kind_explicitly_routes_to_soft_without_trim(self):
        self.chroma_source()
        data = self.report(self.batch([self.asset(alphaSourceKind="chroma", chroma={"keyColor": "#ff00ff", "maxResidueRatio": 0})]))
        result = data["results"][0]
        self.assertEqual(result["alphaMode"], "chroma-soft")
        self.assertEqual(result["report"]["outputAlpha"]["transparentPixels"], 32)
        self.assertEqual(result["report"]["outputSize"], {"width": 8, "height": 6})

    def test_batch_preserves_explicit_zero_alpha_threshold(self):
        image = self.native()
        image.putpixel((0, 0), (20, 30, 40, 1))
        image.save(self.source)
        data = self.report(self.batch([self.asset(alphaThreshold=0)]))
        self.assertEqual(data["results"][0]["report"]["alphaCoverage"], 0.354167)

    def test_batch_rejects_non_boolean_transparent(self):
        result = self.batch([self.asset(transparent="false")])
        self.assertEqual(result.returncode, 1)
        self.assertEqual(json.loads(result.stdout)["results"][0]["error"], "invalid_transparent")

    def test_batch_failure_does_not_copy_to_unity_and_does_not_block_other_asset(self):
        Image.new("RGB", (8, 6), "white").save(self.work / "opaque.png")
        result = self.batch([
            self.asset(id="invalid", repairedAsset="opaque.png", file="bad.png", path="Assets/bad.png"),
            self.asset(id="valid"),
        ])
        self.assertEqual(result.returncode, 1)
        data = json.loads(result.stdout)
        self.assertFalse(data["results"][0]["ok"])
        self.assertTrue(data["results"][1]["ok"])
        self.assertFalse((self.root / "Assets/bad.png").exists())
        self.assertFalse((self.work / "bad.png").exists())
        self.assertTrue(self.output.exists())

    def test_single_and_batch_cannot_overwrite_source(self):
        self.output = self.source
        self.assertNotEqual(self.single("--alpha-required").returncode, 0)
        result = self.batch([self.asset(path="source.png")])
        self.assertEqual(result.returncode, 1)
        self.assertEqual(json.loads(result.stdout)["results"][0]["error"], "source_overwrite")
        self.assert_pixels_equal(self.native(), self.source)

    def test_chroma_residue_failure_does_not_overwrite_output(self):
        self.chroma_source()
        with Image.open(self.source) as image:
            # 该像素落在 soft ramp 内，但与 key 的距离仍小于残留阈值。
            image.putpixel((3, 3), (235, 0, 255))
            image.save(self.source)
        self.output.write_bytes(b"existing output")
        result = self.single("--alpha-required", "--alpha-mode", "chroma-soft",
                             "--chroma-key-color", "#ff00ff", "--transparent-threshold", "0",
                             "--max-chroma-residue-ratio", "0")
        self.assertEqual(result.returncode, 1)
        self.assertIn("残留超限", result.stderr)
        self.assertEqual(self.output.read_bytes(), b"existing output")

    def test_batch_explicit_chroma_trim_and_false_despill(self):
        self.chroma_source()
        with Image.open(self.source) as image:
            image.putpixel((2, 1), (200, 0, 255))
            image.save(self.source)
        data = self.report(self.batch([self.asset(
            alphaSourceKind="chroma", alphaMode="chroma-soft-trim", padding=1,
            chroma={"keyColor": "#ff00ff", "despill": False},
        )]))
        self.assertEqual(data["results"][0]["report"]["outputSize"], {"width": 6, "height": 6})
        with Image.open(self.output) as image:
            self.assertEqual(image.getpixel((1, 1))[:3], (200, 0, 255))

    def test_batch_rejects_legacy_chroma_options_on_native_input(self):
        result = self.batch([self.asset(chroma={"keyColor": "#ff00ff"})])
        self.assertEqual(result.returncode, 1)
        self.assertIn("非扣色输入", json.loads(result.stdout)["results"][0]["stderr"])
        self.assertFalse(self.output.exists())


if __name__ == "__main__":
    unittest.main()
