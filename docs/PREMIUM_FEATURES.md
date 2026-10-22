# Feature access

The retained feature catalog contains world clocks and astronomy. Both are Free features. The existing Free limit allows creating up to three saved clocks; existing selections are preserved when opening settings or changing other preferences. The widget refactor does not implement purchases or infer a license from settings.

`FeatureAccessPolicy` accepts a trusted license source. Until one is configured, the runtime uses the explicit unlicensed state. Its Debug tier simulation is process-local and does not persist a license.

The removed CLI, reports, screenshot scheduling, activity labels and data-transfer workflows no longer have feature definitions or launcher entries. Product and commercial plans remain outside Git.