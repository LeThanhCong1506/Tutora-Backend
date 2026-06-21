-- =====================================================
-- Migration: Rename all tables and columns to snake_case
-- Date: 2026-06-21
-- Description:
--   Standardize the whole public schema to snake_case naming.
--   PostgreSQL auto-updates dependent FKs, indexes, CHECK constraints
--   and identity sequences on RENAME, so no drop/recreate is needed.
--   Constraint/index/sequence *names* are intentionally left unchanged.
--   C# entity property names are NOT changed; EF HasColumnName handles the mapping.
-- =====================================================

BEGIN;

-- ----- Rename tables (flatcase -> snake_case) -----
ALTER TABLE public.chatchannels RENAME TO chat_channels;
ALTER TABLE public.chatmessages RENAME TO chat_messages;
ALTER TABLE public.chatparticipants RENAME TO chat_participants;
ALTER TABLE public.gradelevels RENAME TO grade_levels;
ALTER TABLE public.handoversummaries RENAME TO handover_summaries;
ALTER TABLE public.learningmaterials RENAME TO learning_materials;
ALTER TABLE public.lessonreports RENAME TO lesson_reports;
ALTER TABLE public.profilesuspensions RENAME TO profile_suspensions;
ALTER TABLE public.refreshtokens RENAME TO refresh_tokens;
ALTER TABLE public.studentgrades RENAME TO student_grades;
ALTER TABLE public.studentprofiles RENAME TO student_profiles;
ALTER TABLE public.systemalerts RENAME TO system_alerts;
ALTER TABLE public.systemconfigs RENAME TO system_configs;
ALTER TABLE public.topuprequests RENAME TO topup_requests;
ALTER TABLE public.tutoravailability RENAME TO tutor_availability;
ALTER TABLE public.tutorcertificates RENAME TO tutor_certificates;
ALTER TABLE public.tutorpackagefixedslots RENAME TO tutor_package_fixed_slots;
ALTER TABLE public.tutorpackages RENAME TO tutor_packages;
ALTER TABLE public.tutorprofiles RENAME TO tutor_profiles;
ALTER TABLE public.tutorsubjectgradeprices RENAME TO tutor_subject_grade_prices;
ALTER TABLE public.tutorsubscriptions RENAME TO tutor_subscriptions;
ALTER TABLE public.userwarnings RENAME TO user_warnings;
ALTER TABLE public.wallettransactions RENAME TO wallet_transactions;
ALTER TABLE public.withdrawalrequests RENAME TO withdrawal_requests;

-- ----- Rename columns (flatcase -> snake_case) -----
-- Table: bank_change_logs
ALTER TABLE public.bank_change_logs RENAME COLUMN logid TO log_id;
ALTER TABLE public.bank_change_logs RENAME COLUMN changedat TO changed_at;
ALTER TABLE public.bank_change_logs RENAME COLUMN ipaddress TO ip_address;
ALTER TABLE public.bank_change_logs RENAME COLUMN newbankaccountname TO new_bank_account_name;
ALTER TABLE public.bank_change_logs RENAME COLUMN newbankaccountnumber TO new_bank_account_number;
ALTER TABLE public.bank_change_logs RENAME COLUMN newbankname TO new_bank_name;
ALTER TABLE public.bank_change_logs RENAME COLUMN oldbankaccountname TO old_bank_account_name;
ALTER TABLE public.bank_change_logs RENAME COLUMN oldbankaccountnumber TO old_bank_account_number;
ALTER TABLE public.bank_change_logs RENAME COLUMN oldbankname TO old_bank_name;
ALTER TABLE public.bank_change_logs RENAME COLUMN tutorid TO tutor_id;
ALTER TABLE public.bank_change_logs RENAME COLUMN useragent TO user_agent;

-- Table: bookings
ALTER TABLE public.bookings RENAME COLUMN bookingid TO booking_id;
ALTER TABLE public.bookings RENAME COLUMN parentid TO parent_id;
ALTER TABLE public.bookings RENAME COLUMN studentid TO student_id;
ALTER TABLE public.bookings RENAME COLUMN tutorid TO tutor_id;
ALTER TABLE public.bookings RENAME COLUMN promotionid TO promotion_id;
ALTER TABLE public.bookings RENAME COLUMN discountapplied TO discount_applied;
ALTER TABLE public.bookings RENAME COLUMN sessionsremaining TO sessions_remaining;
ALTER TABLE public.bookings RENAME COLUMN finalprice TO final_price;
ALTER TABLE public.bookings RENAME COLUMN paymentcode TO payment_code;
ALTER TABLE public.bookings RENAME COLUMN locationcity TO location_city;
ALTER TABLE public.bookings RENAME COLUMN locationdistrict TO location_district;
ALTER TABLE public.bookings RENAME COLUMN locationward TO location_ward;
ALTER TABLE public.bookings RENAME COLUMN locationdetail TO location_detail;
ALTER TABLE public.bookings RENAME COLUMN paymentstatus TO payment_status;
ALTER TABLE public.bookings RENAME COLUMN paymentdueat TO payment_due_at;
ALTER TABLE public.bookings RENAME COLUMN createdat TO created_at;
ALTER TABLE public.bookings RENAME COLUMN updatedat TO updated_at;
ALTER TABLE public.bookings RENAME COLUMN depositamount TO deposit_amount;
ALTER TABLE public.bookings RENAME COLUMN depositpaidat TO deposit_paid_at;
ALTER TABLE public.bookings RENAME COLUMN remainingamount TO remaining_amount;
ALTER TABLE public.bookings RENAME COLUMN remainingpaidat TO remaining_paid_at;
ALTER TABLE public.bookings RENAME COLUMN escrowstatus TO escrow_status;
ALTER TABLE public.bookings RENAME COLUMN platformfee TO platform_fee;
ALTER TABLE public.bookings RENAME COLUMN parentfee TO parent_fee;
ALTER TABLE public.bookings RENAME COLUMN tutorfee TO tutor_fee;
ALTER TABLE public.bookings RENAME COLUMN cancellationreason TO cancellation_reason;
ALTER TABLE public.bookings RENAME COLUMN cancelledby TO cancelled_by;
ALTER TABLE public.bookings RENAME COLUMN cancelledat TO cancelled_at;
ALTER TABLE public.bookings RENAME COLUMN graceperiodends TO grace_period_ends;
ALTER TABLE public.bookings RENAME COLUMN startdate TO start_date;
ALTER TABLE public.bookings RENAME COLUMN createdbyrole TO created_by_role;
ALTER TABLE public.bookings RENAME COLUMN refundamount TO refund_amount;
ALTER TABLE public.bookings RENAME COLUMN refundstatus TO refund_status;
ALTER TABLE public.bookings RENAME COLUMN responsedeadline TO response_deadline;
ALTER TABLE public.bookings RENAME COLUMN tutorsubjectgradepriceid TO tutor_subject_grade_price_id;
ALTER TABLE public.bookings RENAME COLUMN packageid TO package_id;
ALTER TABLE public.bookings RENAME COLUMN totalsessions TO total_sessions;
ALTER TABLE public.bookings RENAME COLUMN priceperhour TO price_per_hour;
ALTER TABLE public.bookings RENAME COLUMN totalamount TO total_amount;
ALTER TABLE public.bookings RENAME COLUMN payosbin TO payos_bin;
ALTER TABLE public.bookings RENAME COLUMN payosaccountnumber TO payos_account_number;
ALTER TABLE public.bookings RENAME COLUMN payosaccountname TO payos_account_name;
ALTER TABLE public.bookings RENAME COLUMN payosdescription TO payos_description;
ALTER TABLE public.bookings RENAME COLUMN payoscheckouturl TO payos_checkout_url;
ALTER TABLE public.bookings RENAME COLUMN payosqrcode TO payos_qr_code;

-- Table: chat_channels
ALTER TABLE public.chat_channels RENAME COLUMN channelid TO channel_id;
ALTER TABLE public.chat_channels RENAME COLUMN bookingid TO booking_id;
ALTER TABLE public.chat_channels RENAME COLUMN createdat TO created_at;
ALTER TABLE public.chat_channels RENAME COLUMN lastmessageat TO last_message_at;
ALTER TABLE public.chat_channels RENAME COLUMN parentid TO parent_id;
ALTER TABLE public.chat_channels RENAME COLUMN tutorid TO tutor_id;
ALTER TABLE public.chat_channels RENAME COLUMN studentid TO student_id;

-- Table: chat_messages
ALTER TABLE public.chat_messages RENAME COLUMN messageid TO message_id;
ALTER TABLE public.chat_messages RENAME COLUMN channelid TO channel_id;
ALTER TABLE public.chat_messages RENAME COLUMN senderid TO sender_id;
ALTER TABLE public.chat_messages RENAME COLUMN messagetype TO message_type;
ALTER TABLE public.chat_messages RENAME COLUMN createdat TO created_at;
ALTER TABLE public.chat_messages RENAME COLUMN isread TO is_read;
ALTER TABLE public.chat_messages RENAME COLUMN readat TO read_at;

-- Table: chat_participants
ALTER TABLE public.chat_participants RENAME COLUMN channelid TO channel_id;
ALTER TABLE public.chat_participants RENAME COLUMN userid TO user_id;

-- Table: classes
ALTER TABLE public.classes RENAME COLUMN classid TO class_id;
ALTER TABLE public.classes RENAME COLUMN bookingid TO booking_id;
ALTER TABLE public.classes RENAME COLUMN tutorid TO tutor_id;
ALTER TABLE public.classes RENAME COLUMN classcode TO class_code;
ALTER TABLE public.classes RENAME COLUMN classname TO class_name;
ALTER TABLE public.classes RENAME COLUMN createdat TO created_at;

-- Table: disputes
ALTER TABLE public.disputes RENAME COLUMN disputeid TO dispute_id;
ALTER TABLE public.disputes RENAME COLUMN bookingid TO booking_id;
ALTER TABLE public.disputes RENAME COLUMN lessonid TO lesson_id;
ALTER TABLE public.disputes RENAME COLUMN createdby TO created_by;
ALTER TABLE public.disputes RENAME COLUMN resolvedby TO resolved_by;
ALTER TABLE public.disputes RENAME COLUMN resolutionnote TO resolution_note;
ALTER TABLE public.disputes RENAME COLUMN refundissued TO refund_issued;
ALTER TABLE public.disputes RENAME COLUMN createdat TO created_at;
ALTER TABLE public.disputes RENAME COLUMN disputetype TO dispute_type;
ALTER TABLE public.disputes RENAME COLUMN refundamount TO refund_amount;
ALTER TABLE public.disputes RENAME COLUMN refundpercentage TO refund_percentage;
ALTER TABLE public.disputes RENAME COLUMN resolvedat TO resolved_at;

-- Table: feedbacks
ALTER TABLE public.feedbacks RENAME COLUMN feedbackid TO feedback_id;
ALTER TABLE public.feedbacks RENAME COLUMN bookingid TO booking_id;
ALTER TABLE public.feedbacks RENAME COLUMN fromuserid TO from_user_id;
ALTER TABLE public.feedbacks RENAME COLUMN touserid TO to_user_id;
ALTER TABLE public.feedbacks RENAME COLUMN replycomment TO reply_comment;
ALTER TABLE public.feedbacks RENAME COLUMN repliedat TO replied_at;
ALTER TABLE public.feedbacks RENAME COLUMN isvisible TO is_visible;
ALTER TABLE public.feedbacks RENAME COLUMN createdat TO created_at;
ALTER TABLE public.feedbacks RENAME COLUMN feedbacktype TO feedback_type;
ALTER TABLE public.feedbacks RENAME COLUMN lessonid TO lesson_id;

-- Table: fraud_logs
ALTER TABLE public.fraud_logs RENAME COLUMN logid TO log_id;
ALTER TABLE public.fraud_logs RENAME COLUMN tutorid TO tutor_id;
ALTER TABLE public.fraud_logs RENAME COLUMN withdrawalrequestid TO withdrawal_request_id;
ALTER TABLE public.fraud_logs RENAME COLUMN rulename TO rule_name;
ALTER TABLE public.fraud_logs RENAME COLUMN isflagged TO is_flagged;
ALTER TABLE public.fraud_logs RENAME COLUMN checkedat TO checked_at;

-- Table: grade_levels
ALTER TABLE public.grade_levels RENAME COLUMN gradelevelid TO grade_level_id;
ALTER TABLE public.grade_levels RENAME COLUMN gradename TO grade_name;
ALTER TABLE public.grade_levels RENAME COLUMN levelorder TO level_order;
ALTER TABLE public.grade_levels RENAME COLUMN createdat TO created_at;

-- Table: handover_summaries
ALTER TABLE public.handover_summaries RENAME COLUMN summaryid TO summary_id;
ALTER TABLE public.handover_summaries RENAME COLUMN studentid TO student_id;
ALTER TABLE public.handover_summaries RENAME COLUMN frombookingid TO from_booking_id;
ALTER TABLE public.handover_summaries RENAME COLUMN fromtutorid TO from_tutor_id;
ALTER TABLE public.handover_summaries RENAME COLUMN totutorid TO to_tutor_id;
ALTER TABLE public.handover_summaries RENAME COLUMN totalsessions TO total_sessions;
ALTER TABLE public.handover_summaries RENAME COLUMN attendancerate TO attendance_rate;
ALTER TABLE public.handover_summaries RENAME COLUMN averagescore TO average_score;
ALTER TABLE public.handover_summaries RENAME COLUMN scoretrend TO score_trend;
ALTER TABLE public.handover_summaries RENAME COLUMN topicscovered TO topics_covered;
ALTER TABLE public.handover_summaries RENAME COLUMN tutornotes TO tutor_notes;
ALTER TABLE public.handover_summaries RENAME COLUMN generatedat TO generated_at;

-- Table: learning_materials
ALTER TABLE public.learning_materials RENAME COLUMN materialid TO material_id;
ALTER TABLE public.learning_materials RENAME COLUMN studentid TO student_id;
ALTER TABLE public.learning_materials RENAME COLUMN bookingid TO booking_id;
ALTER TABLE public.learning_materials RENAME COLUMN uploadedby TO uploaded_by;
ALTER TABLE public.learning_materials RENAME COLUMN ownertype TO owner_type;
ALTER TABLE public.learning_materials RENAME COLUMN filetype TO file_type;
ALTER TABLE public.learning_materials RENAME COLUMN fileurl TO file_url;
ALTER TABLE public.learning_materials RENAME COLUMN filesize TO file_size;
ALTER TABLE public.learning_materials RENAME COLUMN ispublic TO is_public;
ALTER TABLE public.learning_materials RENAME COLUMN createdat TO created_at;

-- Table: lesson_reports
ALTER TABLE public.lesson_reports RENAME COLUMN reportid TO report_id;
ALTER TABLE public.lesson_reports RENAME COLUMN lessonid TO lesson_id;
ALTER TABLE public.lesson_reports RENAME COLUMN createdbytutorid TO created_by_tutor_id;
ALTER TABLE public.lesson_reports RENAME COLUMN contentcovered TO content_covered;
ALTER TABLE public.lesson_reports RENAME COLUMN homeworkassigned TO homework_assigned;
ALTER TABLE public.lesson_reports RENAME COLUMN studentperformancerating TO student_performance_rating;
ALTER TABLE public.lesson_reports RENAME COLUMN createdat TO created_at;

-- Table: lessons
ALTER TABLE public.lessons RENAME COLUMN lessonid TO lesson_id;
ALTER TABLE public.lessons RENAME COLUMN bookingid TO booking_id;
ALTER TABLE public.lessons RENAME COLUMN tutorid TO tutor_id;
ALTER TABLE public.lessons RENAME COLUMN studentid TO student_id;
ALTER TABLE public.lessons RENAME COLUMN scheduledstart TO scheduled_start;
ALTER TABLE public.lessons RENAME COLUMN scheduledend TO scheduled_end;
ALTER TABLE public.lessons RENAME COLUMN realstart TO real_start;
ALTER TABLE public.lessons RENAME COLUMN realend TO real_end;
ALTER TABLE public.lessons RENAME COLUMN meetinglink TO meeting_link;
ALTER TABLE public.lessons RENAME COLUMN istutorpresent TO is_tutor_present;
ALTER TABLE public.lessons RENAME COLUMN isstudentpresent TO is_student_present;
ALTER TABLE public.lessons RENAME COLUMN attendancenote TO attendance_note;
ALTER TABLE public.lessons RENAME COLUMN submittedat TO submitted_at;
ALTER TABLE public.lessons RENAME COLUMN confirmdeadline TO confirm_deadline;
ALTER TABLE public.lessons RENAME COLUMN receiptsentat TO receipt_sent_at;
ALTER TABLE public.lessons RENAME COLUMN parentackat TO parent_ack_at;
ALTER TABLE public.lessons RENAME COLUMN lessonprice TO lesson_price;
ALTER TABLE public.lessons RENAME COLUMN issettled TO is_settled;
ALTER TABLE public.lessons RENAME COLUMN checkintime TO check_in_time;
ALTER TABLE public.lessons RENAME COLUMN checkouttime TO check_out_time;
ALTER TABLE public.lessons RENAME COLUMN lessoncontent TO lesson_content;
ALTER TABLE public.lessons RENAME COLUMN tutornotes TO tutor_notes;
ALTER TABLE public.lessons RENAME COLUMN autoreportsent TO auto_report_sent;
ALTER TABLE public.lessons RENAME COLUMN autoreportsentat TO auto_report_sent_at;
ALTER TABLE public.lessons RENAME COLUMN ismakeup TO is_makeup;
ALTER TABLE public.lessons RENAME COLUMN originallessonid TO original_lesson_id;
ALTER TABLE public.lessons RENAME COLUMN noshowaction TO no_show_action;
ALTER TABLE public.lessons RENAME COLUMN createdat TO created_at;
ALTER TABLE public.lessons RENAME COLUMN isearlysubmission TO is_early_submission;

-- Table: login_history
ALTER TABLE public.login_history RENAME COLUMN logid TO log_id;
ALTER TABLE public.login_history RENAME COLUMN userid TO user_id;
ALTER TABLE public.login_history RENAME COLUMN ipaddress TO ip_address;
ALTER TABLE public.login_history RENAME COLUMN useragent TO user_agent;
ALTER TABLE public.login_history RENAME COLUMN loggedat TO logged_at;

-- Table: notifications
ALTER TABLE public.notifications RENAME COLUMN notificationid TO notification_id;
ALTER TABLE public.notifications RENAME COLUMN userid TO user_id;
ALTER TABLE public.notifications RENAME COLUMN referenceid TO reference_id;
ALTER TABLE public.notifications RENAME COLUMN isread TO is_read;
ALTER TABLE public.notifications RENAME COLUMN createdat TO created_at;
ALTER TABLE public.notifications RENAME COLUMN zaborequestid TO zabo_request_id;
ALTER TABLE public.notifications RENAME COLUMN deliverystatus TO delivery_status;

-- Table: profile_suspensions
ALTER TABLE public.profile_suspensions RENAME COLUMN suspensionid TO suspension_id;
ALTER TABLE public.profile_suspensions RENAME COLUMN userid TO user_id;
ALTER TABLE public.profile_suspensions RENAME COLUMN suspensiontype TO suspension_type;
ALTER TABLE public.profile_suspensions RENAME COLUMN startdate TO start_date;
ALTER TABLE public.profile_suspensions RENAME COLUMN enddate TO end_date;
ALTER TABLE public.profile_suspensions RENAME COLUMN isactive TO is_active;
ALTER TABLE public.profile_suspensions RENAME COLUMN createdby TO created_by;

-- Table: promotions
ALTER TABLE public.promotions RENAME COLUMN promotionid TO promotion_id;
ALTER TABLE public.promotions RENAME COLUMN discounttype TO discount_type;
ALTER TABLE public.promotions RENAME COLUMN discountvalue TO discount_value;
ALTER TABLE public.promotions RENAME COLUMN maxdiscountamount TO max_discount_amount;
ALTER TABLE public.promotions RENAME COLUMN minordervalue TO min_order_value;
ALTER TABLE public.promotions RENAME COLUMN startdate TO start_date;
ALTER TABLE public.promotions RENAME COLUMN enddate TO end_date;
ALTER TABLE public.promotions RENAME COLUMN usagelimit TO usage_limit;
ALTER TABLE public.promotions RENAME COLUMN usagecount TO usage_count;
ALTER TABLE public.promotions RENAME COLUMN isactive TO is_active;
ALTER TABLE public.promotions RENAME COLUMN createdat TO created_at;

-- Table: refresh_tokens
ALTER TABLE public.refresh_tokens RENAME COLUMN tokenhash TO token_hash;
ALTER TABLE public.refresh_tokens RENAME COLUMN userid TO user_id;
ALTER TABLE public.refresh_tokens RENAME COLUMN tokenfamily TO token_family;
ALTER TABLE public.refresh_tokens RENAME COLUMN expiresat TO expires_at;
ALTER TABLE public.refresh_tokens RENAME COLUMN createdat TO created_at;
ALTER TABLE public.refresh_tokens RENAME COLUMN revokedat TO revoked_at;
ALTER TABLE public.refresh_tokens RENAME COLUMN replacedbytokenhash TO replaced_by_token_hash;

-- Table: sitefaqs
ALTER TABLE public.sitefaqs RENAME COLUMN faqid TO faq_id;
ALTER TABLE public.sitefaqs RENAME COLUMN displayorder TO display_order;
ALTER TABLE public.sitefaqs RENAME COLUMN isactive TO is_active;
ALTER TABLE public.sitefaqs RENAME COLUMN createdat TO created_at;
ALTER TABLE public.sitefaqs RENAME COLUMN updatedat TO updated_at;
ALTER TABLE public.sitefaqs RENAME COLUMN createdby TO created_by;
ALTER TABLE public.sitefaqs RENAME COLUMN updatedby TO updated_by;

-- Table: student_grades
ALTER TABLE public.student_grades RENAME COLUMN gradeid TO grade_id;
ALTER TABLE public.student_grades RENAME COLUMN studentid TO student_id;
ALTER TABLE public.student_grades RENAME COLUMN bookingid TO booking_id;
ALTER TABLE public.student_grades RENAME COLUMN tutorid TO tutor_id;
ALTER TABLE public.student_grades RENAME COLUMN subjectid TO subject_id;
ALTER TABLE public.student_grades RENAME COLUMN examname TO exam_name;
ALTER TABLE public.student_grades RENAME COLUMN examtype TO exam_type;
ALTER TABLE public.student_grades RENAME COLUMN maxscore TO max_score;
ALTER TABLE public.student_grades RENAME COLUMN examdate TO exam_date;
ALTER TABLE public.student_grades RENAME COLUMN createdat TO created_at;

-- Table: student_profiles
ALTER TABLE public.student_profiles RENAME COLUMN studentid TO student_id;
ALTER TABLE public.student_profiles RENAME COLUMN parentid TO parent_id;
ALTER TABLE public.student_profiles RENAME COLUMN linkeduserid TO linked_user_id;
ALTER TABLE public.student_profiles RENAME COLUMN fullname TO full_name;
ALTER TABLE public.student_profiles RENAME COLUMN birthdate TO birth_date;
ALTER TABLE public.student_profiles RENAME COLUMN gradelevelid TO grade_level_id;
ALTER TABLE public.student_profiles RENAME COLUMN learninggoals TO learning_goals;
ALTER TABLE public.student_profiles RENAME COLUMN avatarurl TO avatar_url;
ALTER TABLE public.student_profiles RENAME COLUMN createdat TO created_at;
ALTER TABLE public.student_profiles RENAME COLUMN studentcode TO student_code;
ALTER TABLE public.student_profiles RENAME COLUMN studentcodeexpiresat TO student_code_expires_at;
ALTER TABLE public.student_profiles RENAME COLUMN deletedat TO deleted_at;

-- Table: subjects
ALTER TABLE public.subjects RENAME COLUMN subjectid TO subject_id;
ALTER TABLE public.subjects RENAME COLUMN subjectname TO subject_name;

-- Table: system_alerts
ALTER TABLE public.system_alerts RENAME COLUMN alertid TO alert_id;
ALTER TABLE public.system_alerts RENAME COLUMN resolvedat TO resolved_at;
ALTER TABLE public.system_alerts RENAME COLUMN createdat TO created_at;
ALTER TABLE public.system_alerts RENAME COLUMN resolvedby TO resolved_by;

-- Table: system_configs
ALTER TABLE public.system_configs RENAME COLUMN configid TO config_id;
ALTER TABLE public.system_configs RENAME COLUMN configkey TO config_key;
ALTER TABLE public.system_configs RENAME COLUMN configvalue TO config_value;
ALTER TABLE public.system_configs RENAME COLUMN updatedby TO updated_by;
ALTER TABLE public.system_configs RENAME COLUMN updatedat TO updated_at;

-- Table: topup_requests
ALTER TABLE public.topup_requests RENAME COLUMN topuprequestid TO topup_request_id;
ALTER TABLE public.topup_requests RENAME COLUMN ordercode TO order_code;
ALTER TABLE public.topup_requests RENAME COLUMN userid TO user_id;
ALTER TABLE public.topup_requests RENAME COLUMN paymentlinkid TO payment_link_id;
ALTER TABLE public.topup_requests RENAME COLUMN createdat TO created_at;
ALTER TABLE public.topup_requests RENAME COLUMN completedat TO completed_at;
ALTER TABLE public.topup_requests RENAME COLUMN expiresat TO expires_at;

-- Table: tutor_availability
ALTER TABLE public.tutor_availability RENAME COLUMN availabilityid TO availability_id;
ALTER TABLE public.tutor_availability RENAME COLUMN tutorid TO tutor_id;
ALTER TABLE public.tutor_availability RENAME COLUMN dayofweek TO day_of_week;
ALTER TABLE public.tutor_availability RENAME COLUMN starttime TO start_time;
ALTER TABLE public.tutor_availability RENAME COLUMN endtime TO end_time;
ALTER TABLE public.tutor_availability RENAME COLUMN createdat TO created_at;

-- Table: tutor_certificates
ALTER TABLE public.tutor_certificates RENAME COLUMN certificateid TO certificate_id;
ALTER TABLE public.tutor_certificates RENAME COLUMN tutorid TO tutor_id;
ALTER TABLE public.tutor_certificates RENAME COLUMN certificatename TO certificate_name;
ALTER TABLE public.tutor_certificates RENAME COLUMN certificatetype TO certificate_type;
ALTER TABLE public.tutor_certificates RENAME COLUMN issuingorganization TO issuing_organization;
ALTER TABLE public.tutor_certificates RENAME COLUMN yearissued TO year_issued;
ALTER TABLE public.tutor_certificates RENAME COLUMN credentialid TO credential_id;
ALTER TABLE public.tutor_certificates RENAME COLUMN credentialurl TO credential_url;
ALTER TABLE public.tutor_certificates RENAME COLUMN certificatefileurl TO certificate_file_url;
ALTER TABLE public.tutor_certificates RENAME COLUMN createdat TO created_at;
ALTER TABLE public.tutor_certificates RENAME COLUMN updatedat TO updated_at;
ALTER TABLE public.tutor_certificates RENAME COLUMN verificationstatus TO verification_status;
ALTER TABLE public.tutor_certificates RENAME COLUMN verificationnote TO verification_note;

-- Table: tutor_package_fixed_slots
ALTER TABLE public.tutor_package_fixed_slots RENAME COLUMN fixedslotid TO fixed_slot_id;
ALTER TABLE public.tutor_package_fixed_slots RENAME COLUMN packageid TO package_id;
ALTER TABLE public.tutor_package_fixed_slots RENAME COLUMN dayofweek TO day_of_week;
ALTER TABLE public.tutor_package_fixed_slots RENAME COLUMN starttime TO start_time;
ALTER TABLE public.tutor_package_fixed_slots RENAME COLUMN endtime TO end_time;
ALTER TABLE public.tutor_package_fixed_slots RENAME COLUMN createdat TO created_at;

-- Table: tutor_packages
ALTER TABLE public.tutor_packages RENAME COLUMN packageid TO package_id;
ALTER TABLE public.tutor_packages RENAME COLUMN tutorid TO tutor_id;
ALTER TABLE public.tutor_packages RENAME COLUMN packagetype TO package_type;
ALTER TABLE public.tutor_packages RENAME COLUMN isactive TO is_active;
ALTER TABLE public.tutor_packages RENAME COLUMN createdat TO created_at;
ALTER TABLE public.tutor_packages RENAME COLUMN updatedat TO updated_at;

-- Table: tutor_profiles
ALTER TABLE public.tutor_profiles RENAME COLUMN tutorid TO tutor_id;
ALTER TABLE public.tutor_profiles RENAME COLUMN videointrourl TO video_intro_url;
ALTER TABLE public.tutor_profiles RENAME COLUMN gpascale TO gpa_scale;
ALTER TABLE public.tutor_profiles RENAME COLUMN teachingareacity TO teaching_area_city;
ALTER TABLE public.tutor_profiles RENAME COLUMN teachingareadistrict TO teaching_area_district;
ALTER TABLE public.tutor_profiles RENAME COLUMN profilestatus TO profile_status;
ALTER TABLE public.tutor_profiles RENAME COLUMN ispublic TO is_public;
ALTER TABLE public.tutor_profiles RENAME COLUMN averagerating TO average_rating;
ALTER TABLE public.tutor_profiles RENAME COLUMN totalreviews TO total_reviews;
ALTER TABLE public.tutor_profiles RENAME COLUMN completedhours TO completed_hours;
ALTER TABLE public.tutor_profiles RENAME COLUMN createdat TO created_at;
ALTER TABLE public.tutor_profiles RENAME COLUMN updatedat TO updated_at;
ALTER TABLE public.tutor_profiles RENAME COLUMN rejectionnote TO rejection_note;
ALTER TABLE public.tutor_profiles RENAME COLUMN subscriptiontype TO subscription_type;
ALTER TABLE public.tutor_profiles RENAME COLUMN deletedat TO deleted_at;
ALTER TABLE public.tutor_profiles RENAME COLUMN reviewedby TO reviewed_by;
ALTER TABLE public.tutor_profiles RENAME COLUMN reviewedat TO reviewed_at;
ALTER TABLE public.tutor_profiles RENAME COLUMN bankname TO bank_name;
ALTER TABLE public.tutor_profiles RENAME COLUMN bankaccountnumber TO bank_account_number;
ALTER TABLE public.tutor_profiles RENAME COLUMN bankaccountname TO bank_account_name;
ALTER TABLE public.tutor_profiles RENAME COLUMN isbankverified TO is_bank_verified;
ALTER TABLE public.tutor_profiles RENAME COLUMN bankchangedat TO bank_changed_at;
ALTER TABLE public.tutor_profiles RENAME COLUMN bankverifiedat TO bank_verified_at;
ALTER TABLE public.tutor_profiles RENAME COLUMN bankverifyattempts TO bank_verify_attempts;
ALTER TABLE public.tutor_profiles RENAME COLUMN bankverifycode TO bank_verify_code;
ALTER TABLE public.tutor_profiles RENAME COLUMN bankverifyrequested TO bank_verify_requested;
ALTER TABLE public.tutor_profiles RENAME COLUMN bankverifystatus TO bank_verify_status;
ALTER TABLE public.tutor_profiles RENAME COLUMN teachingmode TO teaching_mode;

-- Table: tutor_subject_grade_prices
ALTER TABLE public.tutor_subject_grade_prices RENAME COLUMN tutorid TO tutor_id;
ALTER TABLE public.tutor_subject_grade_prices RENAME COLUMN subjectid TO subject_id;
ALTER TABLE public.tutor_subject_grade_prices RENAME COLUMN gradelevelid TO grade_level_id;
ALTER TABLE public.tutor_subject_grade_prices RENAME COLUMN priceperhour TO price_per_hour;
ALTER TABLE public.tutor_subject_grade_prices RENAME COLUMN isactive TO is_active;
ALTER TABLE public.tutor_subject_grade_prices RENAME COLUMN createdat TO created_at;
ALTER TABLE public.tutor_subject_grade_prices RENAME COLUMN updatedat TO updated_at;
ALTER TABLE public.tutor_subject_grade_prices RENAME COLUMN durationminutespersession TO duration_minutes_per_session;
ALTER TABLE public.tutor_subject_grade_prices RENAME COLUMN sessionsperweek TO sessions_per_week;

-- Table: tutor_subscriptions
ALTER TABLE public.tutor_subscriptions RENAME COLUMN subscriptionid TO subscription_id;
ALTER TABLE public.tutor_subscriptions RENAME COLUMN tutorid TO tutor_id;
ALTER TABLE public.tutor_subscriptions RENAME COLUMN packagetype TO package_type;
ALTER TABLE public.tutor_subscriptions RENAME COLUMN startdate TO start_date;
ALTER TABLE public.tutor_subscriptions RENAME COLUMN enddate TO end_date;
ALTER TABLE public.tutor_subscriptions RENAME COLUMN paymentstatus TO payment_status;
ALTER TABLE public.tutor_subscriptions RENAME COLUMN isactive TO is_active;

-- Table: users
ALTER TABLE public.users RENAME COLUMN userid TO user_id;
ALTER TABLE public.users RENAME COLUMN isphoneverified TO is_phone_verified;
ALTER TABLE public.users RENAME COLUMN zalouserid TO zalo_user_id;
ALTER TABLE public.users RENAME COLUMN fullname TO full_name;
ALTER TABLE public.users RENAME COLUMN birthdate TO birth_date;
ALTER TABLE public.users RENAME COLUMN avatarurl TO avatar_url;
ALTER TABLE public.users RENAME COLUMN identitynumber TO identity_number;
ALTER TABLE public.users RENAME COLUMN idcardfronturl TO id_card_front_url;
ALTER TABLE public.users RENAME COLUMN idcardbackurl TO id_card_back_url;
ALTER TABLE public.users RENAME COLUMN isidentityverified TO is_identity_verified;
ALTER TABLE public.users RENAME COLUMN isemailverified TO is_email_verified;
ALTER TABLE public.users RENAME COLUMN lastloginat TO last_login_at;
ALTER TABLE public.users RENAME COLUMN createdat TO created_at;
ALTER TABLE public.users RENAME COLUMN ekycrawdata TO ekyc_raw_data;
ALTER TABLE public.users RENAME COLUMN primaryrole TO primary_role;
ALTER TABLE public.users RENAME COLUMN googlecalendartoken TO google_calendar_token;
ALTER TABLE public.users RENAME COLUMN zabornotifyenabled TO zabo_notify_enabled;
ALTER TABLE public.users RENAME COLUMN parentcode TO parent_code;
ALTER TABLE public.users RENAME COLUMN parentcodeexpiresat TO parent_code_expires_at;
ALTER TABLE public.users RENAME COLUMN hascompletedtour TO has_completed_tour;
ALTER TABLE public.users RENAME COLUMN fcmtoken TO fcm_token;
ALTER TABLE public.users RENAME COLUMN isdeactivated TO is_deactivated;
ALTER TABLE public.users RENAME COLUMN deactivatedat TO deactivated_at;
ALTER TABLE public.users RENAME COLUMN isdeleted TO is_deleted;
ALTER TABLE public.users RENAME COLUMN deletedat TO deleted_at;

-- Table: user_warnings
ALTER TABLE public.user_warnings RENAME COLUMN warningid TO warning_id;
ALTER TABLE public.user_warnings RENAME COLUMN userid TO user_id;
ALTER TABLE public.user_warnings RENAME COLUMN warninglevel TO warning_level;
ALTER TABLE public.user_warnings RENAME COLUMN relatedbookingid TO related_booking_id;
ALTER TABLE public.user_warnings RENAME COLUMN issuedby TO issued_by;
ALTER TABLE public.user_warnings RENAME COLUMN createdat TO created_at;

-- Table: wallets
ALTER TABLE public.wallets RENAME COLUMN walletid TO wallet_id;
ALTER TABLE public.wallets RENAME COLUMN userid TO user_id;
ALTER TABLE public.wallets RENAME COLUMN frozenbalance TO frozen_balance;
ALTER TABLE public.wallets RENAME COLUMN lastupdated TO last_updated;

-- Table: wallet_transactions
ALTER TABLE public.wallet_transactions RENAME COLUMN transactionid TO transaction_id;
ALTER TABLE public.wallet_transactions RENAME COLUMN walletid TO wallet_id;
ALTER TABLE public.wallet_transactions RENAME COLUMN transactiontype TO transaction_type;
ALTER TABLE public.wallet_transactions RENAME COLUMN referenceid TO reference_id;
ALTER TABLE public.wallet_transactions RENAME COLUMN referencetable TO reference_table;
ALTER TABLE public.wallet_transactions RENAME COLUMN createdat TO created_at;
ALTER TABLE public.wallet_transactions RENAME COLUMN ordercode TO order_code;

-- Table: withdrawal_scores
ALTER TABLE public.withdrawal_scores RENAME COLUMN scoreid TO score_id;
ALTER TABLE public.withdrawal_scores RENAME COLUMN withdrawalrequestid TO withdrawal_request_id;
ALTER TABLE public.withdrawal_scores RENAME COLUMN tutorid TO tutor_id;
ALTER TABLE public.withdrawal_scores RENAME COLUMN basescore TO base_score;
ALTER TABLE public.withdrawal_scores RENAME COLUMN positivefactors TO positive_factors;
ALTER TABLE public.withdrawal_scores RENAME COLUMN negativefactors TO negative_factors;
ALTER TABLE public.withdrawal_scores RENAME COLUMN fraudflags TO fraud_flags;
ALTER TABLE public.withdrawal_scores RENAME COLUMN totalscore TO total_score;
ALTER TABLE public.withdrawal_scores RENAME COLUMN createdat TO created_at;

-- Table: withdrawal_requests
ALTER TABLE public.withdrawal_requests RENAME COLUMN withdrawalid TO withdrawal_id;
ALTER TABLE public.withdrawal_requests RENAME COLUMN userid TO user_id;
ALTER TABLE public.withdrawal_requests RENAME COLUMN bankname TO bank_name;
ALTER TABLE public.withdrawal_requests RENAME COLUMN accountnumber TO account_number;
ALTER TABLE public.withdrawal_requests RENAME COLUMN accountholdername TO account_holder_name;
ALTER TABLE public.withdrawal_requests RENAME COLUMN requestedat TO requested_at;
ALTER TABLE public.withdrawal_requests RENAME COLUMN processedat TO processed_at;
ALTER TABLE public.withdrawal_requests RENAME COLUMN payostransactionid TO payos_transaction_id;
ALTER TABLE public.withdrawal_requests RENAME COLUMN payosstatus TO payos_status;
ALTER TABLE public.withdrawal_requests RENAME COLUMN payosresponsecode TO payos_response_code;
ALTER TABLE public.withdrawal_requests RENAME COLUMN payoserror TO payos_error;
ALTER TABLE public.withdrawal_requests RENAME COLUMN retrycount TO retry_count;
ALTER TABLE public.withdrawal_requests RENAME COLUMN lastretryat TO last_retry_at;
ALTER TABLE public.withdrawal_requests RENAME COLUMN processedby TO processed_by;

COMMIT;

