module.exports = {
  branches: [
    "main",
    { name: "dev", channel: "beta", prerelease: "beta" }
  ],
  tagFormat: "v${version}",
  plugins: [
    [
      "@semantic-release/commit-analyzer",
      {
        preset: "conventionalcommits",
        releaseRules: [{ type: "chore", scope: "deps", release: "patch" }]
      }
    ],
    [
      "@semantic-release/release-notes-generator",
      {
        preset: "conventionalcommits",
        presetConfig: {
          types: [
            { type: "feat", section: "Features" },
            { type: "fix", section: "Bug Fixes" },
            { type: "perf", section: "Performance Improvements" },
            { type: "revert", section: "Reverts" },
            { type: "chore", scope: "deps", section: "Dependency Updates" },
            { type: "chore", scope: "deps-ci", section: "Dependency Updates" }
          ]
        }
      }
    ],
    [
      "@semantic-release/exec",
      {
        successCmd: "if [ -n \"$GITHUB_OUTPUT\" ]; then echo \"release_tag=${nextRelease.gitTag}\" >> \"$GITHUB_OUTPUT\"; fi"
      }
    ],
    ["@semantic-release/github", { draftRelease: true }]
  ]
};
