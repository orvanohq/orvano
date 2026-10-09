-- Spec 0013, AC-31: the security_alert template kind (amending spec 0009). A project may edit it like the other
-- auth templates, and its emails show in the log under that name.
ALTER TABLE orvano.messaging_email_templates DROP CONSTRAINT messaging_email_templates_kind_check;
ALTER TABLE orvano.messaging_email_templates ADD CONSTRAINT messaging_email_templates_kind_check
    CHECK (kind IN ('verification', 'recovery', 'magic_link', 'email_code', 'security_alert'));

ALTER TABLE orvano.messaging_emails DROP CONSTRAINT messaging_emails_template_check;
ALTER TABLE orvano.messaging_emails ADD CONSTRAINT messaging_emails_template_check
    CHECK (template IN ('verification', 'recovery', 'magic_link', 'email_code', 'security_alert', 'console_invitation'));
