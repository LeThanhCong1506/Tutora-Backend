--
-- PostgreSQL database dump
--

-- Dumped from database version 17.9
-- Dumped by pg_dump version 17.9

-- Started on 2026-06-05 23:20:36

SET statement_timeout = 0;
SET lock_timeout = 0;
SET idle_in_transaction_session_timeout = 0;
SET transaction_timeout = 0;
SET client_encoding = 'UTF8';
SET standard_conforming_strings = on;
SELECT pg_catalog.set_config('search_path', '', false);
SET check_function_bodies = false;
SET xmloption = content;
SET client_min_messages = warning;
SET row_security = off;

--
-- TOC entry 8 (class 2615 OID 279184)
-- Name: hangfire; Type: SCHEMA; Schema: -; Owner: postgres
--

CREATE SCHEMA hangfire;


ALTER SCHEMA hangfire OWNER TO postgres;

--
-- TOC entry 9 (class 2615 OID 2200)
-- Name: public; Type: SCHEMA; Schema: -; Owner: postgres
--

-- *not* creating schema, since initdb creates it


ALTER SCHEMA public OWNER TO postgres;

--
-- TOC entry 5770 (class 0 OID 0)
-- Dependencies: 9
-- Name: SCHEMA public; Type: COMMENT; Schema: -; Owner: postgres
--

COMMENT ON SCHEMA public IS '';


--
-- TOC entry 2 (class 3079 OID 279185)
-- Name: pg_trgm; Type: EXTENSION; Schema: -; Owner: -
--

CREATE EXTENSION IF NOT EXISTS pg_trgm WITH SCHEMA public;


--
-- TOC entry 5772 (class 0 OID 0)
-- Dependencies: 2
-- Name: EXTENSION pg_trgm; Type: COMMENT; Schema: -; Owner: 
--

COMMENT ON EXTENSION pg_trgm IS 'text similarity measurement and index searching based on trigrams';


--
-- TOC entry 3 (class 3079 OID 279266)
-- Name: pgcrypto; Type: EXTENSION; Schema: -; Owner: -
--

CREATE EXTENSION IF NOT EXISTS pgcrypto WITH SCHEMA public;


--
-- TOC entry 5773 (class 0 OID 0)
-- Dependencies: 3
-- Name: EXTENSION pgcrypto; Type: COMMENT; Schema: -; Owner: 
--

COMMENT ON EXTENSION pgcrypto IS 'cryptographic functions';


--
-- TOC entry 4 (class 3079 OID 279303)
-- Name: unaccent; Type: EXTENSION; Schema: -; Owner: -
--

CREATE EXTENSION IF NOT EXISTS unaccent WITH SCHEMA public;


--
-- TOC entry 5774 (class 0 OID 0)
-- Dependencies: 4
-- Name: EXTENSION unaccent; Type: COMMENT; Schema: -; Owner: 
--

COMMENT ON EXTENSION unaccent IS 'text search dictionary that removes accents';


--
-- TOC entry 327 (class 1255 OID 279310)
-- Name: immutable_unaccent(text); Type: FUNCTION; Schema: public; Owner: postgres
--

CREATE FUNCTION public.immutable_unaccent(text) RETURNS text
    LANGUAGE sql IMMUTABLE STRICT PARALLEL SAFE
    AS $_$
  SELECT public.unaccent($1);
$_$;


ALTER FUNCTION public.immutable_unaccent(text) OWNER TO postgres;

SET default_tablespace = '';

SET default_table_access_method = heap;

--
-- TOC entry 221 (class 1259 OID 279311)
-- Name: aggregatedcounter; Type: TABLE; Schema: hangfire; Owner: postgres
--

CREATE TABLE hangfire.aggregatedcounter (
    id bigint NOT NULL,
    key text NOT NULL,
    value bigint NOT NULL,
    expireat timestamp with time zone
);


ALTER TABLE hangfire.aggregatedcounter OWNER TO postgres;

--
-- TOC entry 222 (class 1259 OID 279316)
-- Name: aggregatedcounter_id_seq; Type: SEQUENCE; Schema: hangfire; Owner: postgres
--

CREATE SEQUENCE hangfire.aggregatedcounter_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE hangfire.aggregatedcounter_id_seq OWNER TO postgres;

--
-- TOC entry 5775 (class 0 OID 0)
-- Dependencies: 222
-- Name: aggregatedcounter_id_seq; Type: SEQUENCE OWNED BY; Schema: hangfire; Owner: postgres
--

ALTER SEQUENCE hangfire.aggregatedcounter_id_seq OWNED BY hangfire.aggregatedcounter.id;


--
-- TOC entry 223 (class 1259 OID 279317)
-- Name: counter; Type: TABLE; Schema: hangfire; Owner: postgres
--

CREATE TABLE hangfire.counter (
    id bigint NOT NULL,
    key text NOT NULL,
    value bigint NOT NULL,
    expireat timestamp with time zone
);


ALTER TABLE hangfire.counter OWNER TO postgres;

--
-- TOC entry 224 (class 1259 OID 279322)
-- Name: counter_id_seq; Type: SEQUENCE; Schema: hangfire; Owner: postgres
--

CREATE SEQUENCE hangfire.counter_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE hangfire.counter_id_seq OWNER TO postgres;

--
-- TOC entry 5776 (class 0 OID 0)
-- Dependencies: 224
-- Name: counter_id_seq; Type: SEQUENCE OWNED BY; Schema: hangfire; Owner: postgres
--

ALTER SEQUENCE hangfire.counter_id_seq OWNED BY hangfire.counter.id;


--
-- TOC entry 225 (class 1259 OID 279323)
-- Name: hash; Type: TABLE; Schema: hangfire; Owner: postgres
--

CREATE TABLE hangfire.hash (
    id bigint NOT NULL,
    key text NOT NULL,
    field text NOT NULL,
    value text,
    expireat timestamp with time zone,
    updatecount integer DEFAULT 0 NOT NULL
);


ALTER TABLE hangfire.hash OWNER TO postgres;

--
-- TOC entry 226 (class 1259 OID 279329)
-- Name: hash_id_seq; Type: SEQUENCE; Schema: hangfire; Owner: postgres
--

CREATE SEQUENCE hangfire.hash_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE hangfire.hash_id_seq OWNER TO postgres;

--
-- TOC entry 5777 (class 0 OID 0)
-- Dependencies: 226
-- Name: hash_id_seq; Type: SEQUENCE OWNED BY; Schema: hangfire; Owner: postgres
--

ALTER SEQUENCE hangfire.hash_id_seq OWNED BY hangfire.hash.id;


--
-- TOC entry 227 (class 1259 OID 279330)
-- Name: job; Type: TABLE; Schema: hangfire; Owner: postgres
--

CREATE TABLE hangfire.job (
    id bigint NOT NULL,
    stateid bigint,
    statename text,
    invocationdata jsonb NOT NULL,
    arguments jsonb NOT NULL,
    createdat timestamp with time zone NOT NULL,
    expireat timestamp with time zone,
    updatecount integer DEFAULT 0 NOT NULL
);


ALTER TABLE hangfire.job OWNER TO postgres;

--
-- TOC entry 228 (class 1259 OID 279336)
-- Name: job_id_seq; Type: SEQUENCE; Schema: hangfire; Owner: postgres
--

CREATE SEQUENCE hangfire.job_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE hangfire.job_id_seq OWNER TO postgres;

--
-- TOC entry 5778 (class 0 OID 0)
-- Dependencies: 228
-- Name: job_id_seq; Type: SEQUENCE OWNED BY; Schema: hangfire; Owner: postgres
--

ALTER SEQUENCE hangfire.job_id_seq OWNED BY hangfire.job.id;


--
-- TOC entry 229 (class 1259 OID 279337)
-- Name: jobparameter; Type: TABLE; Schema: hangfire; Owner: postgres
--

CREATE TABLE hangfire.jobparameter (
    id bigint NOT NULL,
    jobid bigint NOT NULL,
    name text NOT NULL,
    value text,
    updatecount integer DEFAULT 0 NOT NULL
);


ALTER TABLE hangfire.jobparameter OWNER TO postgres;

--
-- TOC entry 230 (class 1259 OID 279343)
-- Name: jobparameter_id_seq; Type: SEQUENCE; Schema: hangfire; Owner: postgres
--

CREATE SEQUENCE hangfire.jobparameter_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE hangfire.jobparameter_id_seq OWNER TO postgres;

--
-- TOC entry 5779 (class 0 OID 0)
-- Dependencies: 230
-- Name: jobparameter_id_seq; Type: SEQUENCE OWNED BY; Schema: hangfire; Owner: postgres
--

ALTER SEQUENCE hangfire.jobparameter_id_seq OWNED BY hangfire.jobparameter.id;


--
-- TOC entry 231 (class 1259 OID 279344)
-- Name: jobqueue; Type: TABLE; Schema: hangfire; Owner: postgres
--

CREATE TABLE hangfire.jobqueue (
    id bigint NOT NULL,
    jobid bigint NOT NULL,
    queue text NOT NULL,
    fetchedat timestamp with time zone,
    updatecount integer DEFAULT 0 NOT NULL
);


ALTER TABLE hangfire.jobqueue OWNER TO postgres;

--
-- TOC entry 232 (class 1259 OID 279350)
-- Name: jobqueue_id_seq; Type: SEQUENCE; Schema: hangfire; Owner: postgres
--

CREATE SEQUENCE hangfire.jobqueue_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE hangfire.jobqueue_id_seq OWNER TO postgres;

--
-- TOC entry 5780 (class 0 OID 0)
-- Dependencies: 232
-- Name: jobqueue_id_seq; Type: SEQUENCE OWNED BY; Schema: hangfire; Owner: postgres
--

ALTER SEQUENCE hangfire.jobqueue_id_seq OWNED BY hangfire.jobqueue.id;


--
-- TOC entry 233 (class 1259 OID 279351)
-- Name: list; Type: TABLE; Schema: hangfire; Owner: postgres
--

CREATE TABLE hangfire.list (
    id bigint NOT NULL,
    key text NOT NULL,
    value text,
    expireat timestamp with time zone,
    updatecount integer DEFAULT 0 NOT NULL
);


ALTER TABLE hangfire.list OWNER TO postgres;

--
-- TOC entry 234 (class 1259 OID 279357)
-- Name: list_id_seq; Type: SEQUENCE; Schema: hangfire; Owner: postgres
--

CREATE SEQUENCE hangfire.list_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE hangfire.list_id_seq OWNER TO postgres;

--
-- TOC entry 5781 (class 0 OID 0)
-- Dependencies: 234
-- Name: list_id_seq; Type: SEQUENCE OWNED BY; Schema: hangfire; Owner: postgres
--

ALTER SEQUENCE hangfire.list_id_seq OWNED BY hangfire.list.id;


--
-- TOC entry 235 (class 1259 OID 279358)
-- Name: lock; Type: TABLE; Schema: hangfire; Owner: postgres
--

CREATE TABLE hangfire.lock (
    resource text NOT NULL,
    updatecount integer DEFAULT 0 NOT NULL,
    acquired timestamp with time zone
);


ALTER TABLE hangfire.lock OWNER TO postgres;

--
-- TOC entry 236 (class 1259 OID 279364)
-- Name: schema; Type: TABLE; Schema: hangfire; Owner: postgres
--

CREATE TABLE hangfire.schema (
    version integer NOT NULL
);


ALTER TABLE hangfire.schema OWNER TO postgres;

--
-- TOC entry 237 (class 1259 OID 279367)
-- Name: server; Type: TABLE; Schema: hangfire; Owner: postgres
--

CREATE TABLE hangfire.server (
    id text NOT NULL,
    data jsonb,
    lastheartbeat timestamp with time zone NOT NULL,
    updatecount integer DEFAULT 0 NOT NULL
);


ALTER TABLE hangfire.server OWNER TO postgres;

--
-- TOC entry 238 (class 1259 OID 279373)
-- Name: set; Type: TABLE; Schema: hangfire; Owner: postgres
--

CREATE TABLE hangfire.set (
    id bigint NOT NULL,
    key text NOT NULL,
    score double precision NOT NULL,
    value text NOT NULL,
    expireat timestamp with time zone,
    updatecount integer DEFAULT 0 NOT NULL
);


ALTER TABLE hangfire.set OWNER TO postgres;

--
-- TOC entry 239 (class 1259 OID 279379)
-- Name: set_id_seq; Type: SEQUENCE; Schema: hangfire; Owner: postgres
--

CREATE SEQUENCE hangfire.set_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE hangfire.set_id_seq OWNER TO postgres;

--
-- TOC entry 5782 (class 0 OID 0)
-- Dependencies: 239
-- Name: set_id_seq; Type: SEQUENCE OWNED BY; Schema: hangfire; Owner: postgres
--

ALTER SEQUENCE hangfire.set_id_seq OWNED BY hangfire.set.id;


--
-- TOC entry 240 (class 1259 OID 279380)
-- Name: state; Type: TABLE; Schema: hangfire; Owner: postgres
--

CREATE TABLE hangfire.state (
    id bigint NOT NULL,
    jobid bigint NOT NULL,
    name text NOT NULL,
    reason text,
    createdat timestamp with time zone NOT NULL,
    data jsonb,
    updatecount integer DEFAULT 0 NOT NULL
);


ALTER TABLE hangfire.state OWNER TO postgres;

--
-- TOC entry 241 (class 1259 OID 279386)
-- Name: state_id_seq; Type: SEQUENCE; Schema: hangfire; Owner: postgres
--

CREATE SEQUENCE hangfire.state_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE hangfire.state_id_seq OWNER TO postgres;

--
-- TOC entry 5783 (class 0 OID 0)
-- Dependencies: 241
-- Name: state_id_seq; Type: SEQUENCE OWNED BY; Schema: hangfire; Owner: postgres
--

ALTER SEQUENCE hangfire.state_id_seq OWNED BY hangfire.state.id;


--
-- TOC entry 242 (class 1259 OID 279387)
-- Name: __EFMigrationsHistory; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL
);


ALTER TABLE public."__EFMigrationsHistory" OWNER TO postgres;

--
-- TOC entry 243 (class 1259 OID 279390)
-- Name: bank_change_logs; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.bank_change_logs (
    logid integer NOT NULL,
    changedat timestamp without time zone NOT NULL,
    ipaddress character varying(45),
    newbankaccountname character varying(200),
    newbankaccountnumber character varying(50),
    newbankname character varying(100),
    oldbankaccountname character varying(200),
    oldbankaccountnumber character varying(50),
    oldbankname character varying(100),
    reason character varying(500),
    tutorid character varying(50) NOT NULL,
    useragent text
);


ALTER TABLE public.bank_change_logs OWNER TO postgres;

--
-- TOC entry 244 (class 1259 OID 279395)
-- Name: bank_change_logs_logid_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

CREATE SEQUENCE public.bank_change_logs_logid_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE public.bank_change_logs_logid_seq OWNER TO postgres;

--
-- TOC entry 5784 (class 0 OID 0)
-- Dependencies: 244
-- Name: bank_change_logs_logid_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: postgres
--

ALTER SEQUENCE public.bank_change_logs_logid_seq OWNED BY public.bank_change_logs.logid;


--
-- TOC entry 245 (class 1259 OID 279396)
-- Name: bookings; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.bookings (
    bookingid integer NOT NULL,
    parentid character varying(50),
    studentid character varying(50),
    tutorid character varying(50),
    promotionid integer,
    discountapplied numeric(12,2) DEFAULT 0,
    sessionsremaining integer,
    finalprice numeric(12,2),
    paymentcode character varying(50),
    locationcity character varying(50),
    locationdistrict character varying(50),
    locationward character varying(50),
    locationdetail character varying(255),
    status character varying(30),
    paymentstatus character varying(20),
    paymentdueat timestamp without time zone,
    createdat timestamp without time zone DEFAULT CURRENT_TIMESTAMP,
    updatedat timestamp without time zone DEFAULT CURRENT_TIMESTAMP,
    depositamount numeric(12,2),
    depositpaidat timestamp without time zone,
    remainingamount numeric(12,2),
    remainingpaidat timestamp without time zone,
    escrowstatus character varying(30),
    platformfee numeric(12,2),
    parentfee numeric(12,2),
    tutorfee numeric(12,2),
    cancellationreason text,
    cancelledby character varying(50),
    cancelledat timestamp without time zone,
    graceperiodends timestamp without time zone,
    startdate timestamp without time zone,
    createdbyrole character varying(20),
    refundamount numeric,
    refundstatus character varying(50),
    responsedeadline timestamp without time zone,
    tutorsubjectgradepriceid integer,
    packageid integer,
    totalsessions integer,
    durationminutespersession integer,
    priceperhour numeric(12,2),
    totalamount numeric(12,2),
    currency character varying(10) DEFAULT 'VND'::character varying
);


ALTER TABLE public.bookings OWNER TO postgres;

--
-- TOC entry 5785 (class 0 OID 0)
-- Dependencies: 245
-- Name: COLUMN bookings.tutorsubjectgradepriceid; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public.bookings.tutorsubjectgradepriceid IS 'Subject + grade + price duoc chon tai thoi diem booking.';


--
-- TOC entry 5786 (class 0 OID 0)
-- Dependencies: 245
-- Name: COLUMN bookings.packageid; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public.bookings.packageid IS 'Package duoc chon tai thoi diem booking.';


--
-- TOC entry 246 (class 1259 OID 279404)
-- Name: bookings_bookingid_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

CREATE SEQUENCE public.bookings_bookingid_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE public.bookings_bookingid_seq OWNER TO postgres;

--
-- TOC entry 5787 (class 0 OID 0)
-- Dependencies: 246
-- Name: bookings_bookingid_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: postgres
--

ALTER SEQUENCE public.bookings_bookingid_seq OWNED BY public.bookings.bookingid;


--
-- TOC entry 247 (class 1259 OID 279405)
-- Name: chatchannels; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.chatchannels (
    channelid integer NOT NULL,
    bookingid integer,
    createdat timestamp without time zone DEFAULT CURRENT_TIMESTAMP,
    lastmessageat timestamp without time zone,
    status character varying(20) DEFAULT 'active'::character varying,
    parentid character varying(50),
    tutorid character varying(50),
    studentid character varying(50)
);


ALTER TABLE public.chatchannels OWNER TO postgres;

--
-- TOC entry 248 (class 1259 OID 279410)
-- Name: chatchannels_channelid_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

CREATE SEQUENCE public.chatchannels_channelid_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE public.chatchannels_channelid_seq OWNER TO postgres;

--
-- TOC entry 5788 (class 0 OID 0)
-- Dependencies: 248
-- Name: chatchannels_channelid_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: postgres
--

ALTER SEQUENCE public.chatchannels_channelid_seq OWNED BY public.chatchannels.channelid;


--
-- TOC entry 249 (class 1259 OID 279411)
-- Name: chatmessages; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.chatmessages (
    messageid integer NOT NULL,
    channelid integer,
    senderid character varying(50),
    content text,
    messagetype character varying(20) DEFAULT 'text'::character varying,
    createdat timestamp without time zone DEFAULT CURRENT_TIMESTAMP,
    metadata jsonb,
    isread boolean DEFAULT false NOT NULL,
    readat timestamp without time zone
);


ALTER TABLE public.chatmessages OWNER TO postgres;

--
-- TOC entry 250 (class 1259 OID 279419)
-- Name: chatmessages_messageid_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

CREATE SEQUENCE public.chatmessages_messageid_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE public.chatmessages_messageid_seq OWNER TO postgres;

--
-- TOC entry 5789 (class 0 OID 0)
-- Dependencies: 250
-- Name: chatmessages_messageid_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: postgres
--

ALTER SEQUENCE public.chatmessages_messageid_seq OWNED BY public.chatmessages.messageid;


--
-- TOC entry 251 (class 1259 OID 279420)
-- Name: chatparticipants; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.chatparticipants (
    channelid integer NOT NULL,
    userid character varying(50) NOT NULL
);


ALTER TABLE public.chatparticipants OWNER TO postgres;

--
-- TOC entry 252 (class 1259 OID 279423)
-- Name: classes; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.classes (
    classid integer NOT NULL,
    bookingid integer,
    tutorid character varying(50),
    classcode character varying(20) NOT NULL,
    classname character varying(200),
    status character varying(20) DEFAULT 'active'::character varying,
    createdat timestamp without time zone DEFAULT CURRENT_TIMESTAMP
);


ALTER TABLE public.classes OWNER TO postgres;

--
-- TOC entry 253 (class 1259 OID 279428)
-- Name: classes_classid_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

CREATE SEQUENCE public.classes_classid_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE public.classes_classid_seq OWNER TO postgres;

--
-- TOC entry 5790 (class 0 OID 0)
-- Dependencies: 253
-- Name: classes_classid_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: postgres
--

ALTER SEQUENCE public.classes_classid_seq OWNED BY public.classes.classid;


--
-- TOC entry 254 (class 1259 OID 279429)
-- Name: disputes; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.disputes (
    disputeid integer NOT NULL,
    bookingid integer,
    lessonid integer,
    createdby character varying(50),
    reason text,
    status character varying(50),
    resolvedby character varying(50),
    resolutionnote text,
    refundissued boolean DEFAULT false,
    createdat timestamp without time zone DEFAULT CURRENT_TIMESTAMP,
    disputetype character varying(50),
    evidence jsonb,
    refundamount numeric(12,2),
    refundpercentage integer,
    resolvedat timestamp without time zone
);


ALTER TABLE public.disputes OWNER TO postgres;

--
-- TOC entry 255 (class 1259 OID 279436)
-- Name: disputes_disputeid_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

CREATE SEQUENCE public.disputes_disputeid_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE public.disputes_disputeid_seq OWNER TO postgres;

--
-- TOC entry 5791 (class 0 OID 0)
-- Dependencies: 255
-- Name: disputes_disputeid_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: postgres
--

ALTER SEQUENCE public.disputes_disputeid_seq OWNED BY public.disputes.disputeid;


--
-- TOC entry 256 (class 1259 OID 279437)
-- Name: feedbacks; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.feedbacks (
    feedbackid integer NOT NULL,
    bookingid integer,
    fromuserid character varying(50),
    touserid character varying(50),
    rating integer,
    comment text,
    replycomment text,
    repliedat timestamp without time zone,
    isvisible boolean DEFAULT true,
    createdat timestamp without time zone DEFAULT CURRENT_TIMESTAMP,
    feedbacktype character varying(30) DEFAULT 'post_lesson'::character varying,
    lessonid integer,
    initial_goal text,
    actual_result text,
    course_duration character varying(50),
    CONSTRAINT feedbacks_rating_check CHECK (((rating >= 1) AND (rating <= 5)))
);


ALTER TABLE public.feedbacks OWNER TO postgres;

--
-- TOC entry 5792 (class 0 OID 0)
-- Dependencies: 256
-- Name: COLUMN feedbacks.initial_goal; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public.feedbacks.initial_goal IS 'Mục tiêu ban đầu của học sinh (Success Diary)';


--
-- TOC entry 5793 (class 0 OID 0)
-- Dependencies: 256
-- Name: COLUMN feedbacks.actual_result; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public.feedbacks.actual_result IS 'Thành tích thực tế đạt được sau khóa học';


--
-- TOC entry 5794 (class 0 OID 0)
-- Dependencies: 256
-- Name: COLUMN feedbacks.course_duration; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public.feedbacks.course_duration IS 'Khoảng thời gian học tập (ví dụ: 6 tháng)';


--
-- TOC entry 257 (class 1259 OID 279446)
-- Name: feedbacks_feedbackid_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

CREATE SEQUENCE public.feedbacks_feedbackid_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE public.feedbacks_feedbackid_seq OWNER TO postgres;

--
-- TOC entry 5795 (class 0 OID 0)
-- Dependencies: 257
-- Name: feedbacks_feedbackid_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: postgres
--

ALTER SEQUENCE public.feedbacks_feedbackid_seq OWNED BY public.feedbacks.feedbackid;


--
-- TOC entry 258 (class 1259 OID 279447)
-- Name: fraud_logs; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.fraud_logs (
    logid integer NOT NULL,
    tutorid character varying(50) NOT NULL,
    withdrawalrequestid integer,
    rulename character varying(100) NOT NULL,
    passed boolean DEFAULT false NOT NULL,
    isflagged boolean DEFAULT false NOT NULL,
    message text,
    metadata jsonb,
    checkedat timestamp without time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);


ALTER TABLE public.fraud_logs OWNER TO postgres;

--
-- TOC entry 5796 (class 0 OID 0)
-- Dependencies: 258
-- Name: TABLE fraud_logs; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON TABLE public.fraud_logs IS 'Audit trail for fraud detection rule executions';


--
-- TOC entry 259 (class 1259 OID 279455)
-- Name: fraud_logs_logid_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

CREATE SEQUENCE public.fraud_logs_logid_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE public.fraud_logs_logid_seq OWNER TO postgres;

--
-- TOC entry 5797 (class 0 OID 0)
-- Dependencies: 259
-- Name: fraud_logs_logid_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: postgres
--

ALTER SEQUENCE public.fraud_logs_logid_seq OWNED BY public.fraud_logs.logid;


--
-- TOC entry 306 (class 1259 OID 295568)
-- Name: gradelevels; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.gradelevels (
    gradelevelid integer NOT NULL,
    gradename character varying(100) NOT NULL,
    levelorder integer NOT NULL,
    createdat timestamp without time zone DEFAULT now()
);


ALTER TABLE public.gradelevels OWNER TO postgres;

--
-- TOC entry 5798 (class 0 OID 0)
-- Dependencies: 306
-- Name: TABLE gradelevels; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON TABLE public.gradelevels IS 'Chuan hoa khoi lop Lop 1-12 cho luong Tutor Setup va Parent Booking.';


--
-- TOC entry 305 (class 1259 OID 295567)
-- Name: gradelevels_gradelevelid_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

ALTER TABLE public.gradelevels ALTER COLUMN gradelevelid ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public.gradelevels_gradelevelid_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- TOC entry 260 (class 1259 OID 279456)
-- Name: handoversummaries; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.handoversummaries (
    summaryid integer NOT NULL,
    studentid character varying(50),
    frombookingid integer,
    fromtutorid character varying(50),
    totutorid character varying(50),
    totalsessions integer,
    attendancerate numeric(5,2),
    averagescore numeric(5,2),
    scoretrend character varying(20),
    topicscovered text,
    tutornotes text,
    generatedat timestamp without time zone DEFAULT CURRENT_TIMESTAMP
);


ALTER TABLE public.handoversummaries OWNER TO postgres;

--
-- TOC entry 261 (class 1259 OID 279462)
-- Name: handoversummaries_summaryid_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

CREATE SEQUENCE public.handoversummaries_summaryid_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE public.handoversummaries_summaryid_seq OWNER TO postgres;

--
-- TOC entry 5799 (class 0 OID 0)
-- Dependencies: 261
-- Name: handoversummaries_summaryid_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: postgres
--

ALTER SEQUENCE public.handoversummaries_summaryid_seq OWNED BY public.handoversummaries.summaryid;


--
-- TOC entry 262 (class 1259 OID 279463)
-- Name: learningmaterials; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.learningmaterials (
    materialid integer NOT NULL,
    studentid character varying(50),
    bookingid integer,
    uploadedby character varying(50),
    ownertype character varying(20) NOT NULL,
    title character varying(255) NOT NULL,
    description text,
    filetype character varying(50),
    fileurl text NOT NULL,
    filesize integer,
    ispublic boolean DEFAULT false,
    createdat timestamp without time zone DEFAULT CURRENT_TIMESTAMP
);


ALTER TABLE public.learningmaterials OWNER TO postgres;

--
-- TOC entry 263 (class 1259 OID 279470)
-- Name: learningmaterials_materialid_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

CREATE SEQUENCE public.learningmaterials_materialid_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE public.learningmaterials_materialid_seq OWNER TO postgres;

--
-- TOC entry 5800 (class 0 OID 0)
-- Dependencies: 263
-- Name: learningmaterials_materialid_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: postgres
--

ALTER SEQUENCE public.learningmaterials_materialid_seq OWNED BY public.learningmaterials.materialid;


--
-- TOC entry 264 (class 1259 OID 279471)
-- Name: lessonreports; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.lessonreports (
    reportid integer NOT NULL,
    lessonid integer,
    createdbytutorid character varying(50),
    contentcovered text,
    homeworkassigned text,
    studentperformancerating integer,
    attachments jsonb,
    createdat timestamp without time zone DEFAULT CURRENT_TIMESTAMP
);


ALTER TABLE public.lessonreports OWNER TO postgres;

--
-- TOC entry 265 (class 1259 OID 279477)
-- Name: lessonreports_reportid_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

CREATE SEQUENCE public.lessonreports_reportid_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE public.lessonreports_reportid_seq OWNER TO postgres;

--
-- TOC entry 5801 (class 0 OID 0)
-- Dependencies: 265
-- Name: lessonreports_reportid_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: postgres
--

ALTER SEQUENCE public.lessonreports_reportid_seq OWNED BY public.lessonreports.reportid;


--
-- TOC entry 266 (class 1259 OID 279478)
-- Name: lessons; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.lessons (
    lessonid integer NOT NULL,
    bookingid integer,
    tutorid character varying(50),
    studentid character varying(50),
    scheduledstart timestamp without time zone NOT NULL,
    scheduledend timestamp without time zone NOT NULL,
    realstart timestamp without time zone,
    realend timestamp without time zone,
    meetinglink character varying(1000),
    istutorpresent boolean,
    isstudentpresent boolean,
    attendancenote text,
    status character varying(30) DEFAULT 'scheduled'::character varying,
    submittedat timestamp without time zone,
    confirmdeadline timestamp without time zone,
    receiptsentat timestamp without time zone,
    parentackat timestamp without time zone,
    lessonprice numeric(12,2),
    issettled boolean DEFAULT false,
    checkintime timestamp without time zone,
    checkouttime timestamp without time zone,
    lessoncontent text,
    homework text,
    tutornotes text,
    autoreportsent boolean DEFAULT false,
    autoreportsentat timestamp without time zone,
    ismakeup boolean DEFAULT false,
    originallessonid integer,
    noshowaction character varying(30),
    createdat timestamp without time zone DEFAULT CURRENT_TIMESTAMP,
    isearlysubmission boolean
);


ALTER TABLE public.lessons OWNER TO postgres;

--
-- TOC entry 5802 (class 0 OID 0)
-- Dependencies: 266
-- Name: TABLE lessons; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON TABLE public.lessons IS 'Lessons la lich hoc that cua booking. Moi record trong lessons la mot buoi hoc that.';


--
-- TOC entry 5803 (class 0 OID 0)
-- Dependencies: 266
-- Name: COLUMN lessons.createdat; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public.lessons.createdat IS 'Timestamp when lesson was created (auto-created after payment)';


--
-- TOC entry 267 (class 1259 OID 279488)
-- Name: lessons_lessonid_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

CREATE SEQUENCE public.lessons_lessonid_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE public.lessons_lessonid_seq OWNER TO postgres;

--
-- TOC entry 5804 (class 0 OID 0)
-- Dependencies: 267
-- Name: lessons_lessonid_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: postgres
--

ALTER SEQUENCE public.lessons_lessonid_seq OWNED BY public.lessons.lessonid;


--
-- TOC entry 268 (class 1259 OID 279489)
-- Name: login_history; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.login_history (
    logid integer NOT NULL,
    userid character varying(50) NOT NULL,
    ipaddress character varying(45) NOT NULL,
    useragent text,
    loggedat timestamp without time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);


ALTER TABLE public.login_history OWNER TO postgres;

--
-- TOC entry 5805 (class 0 OID 0)
-- Dependencies: 268
-- Name: TABLE login_history; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON TABLE public.login_history IS 'Login history for fraud pattern detection';


--
-- TOC entry 269 (class 1259 OID 279495)
-- Name: login_history_logid_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

CREATE SEQUENCE public.login_history_logid_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE public.login_history_logid_seq OWNER TO postgres;

--
-- TOC entry 5806 (class 0 OID 0)
-- Dependencies: 269
-- Name: login_history_logid_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: postgres
--

ALTER SEQUENCE public.login_history_logid_seq OWNED BY public.login_history.logid;


--
-- TOC entry 270 (class 1259 OID 279496)
-- Name: notifications; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.notifications (
    notificationid integer NOT NULL,
    userid character varying(50),
    title character varying(200),
    message text,
    type character varying(50),
    referenceid character varying(50),
    isread boolean DEFAULT false,
    createdat timestamp without time zone DEFAULT CURRENT_TIMESTAMP,
    channel character varying(30) DEFAULT 'app'::character varying,
    zaborequestid character varying(100),
    deliverystatus character varying(20)
);


ALTER TABLE public.notifications OWNER TO postgres;

--
-- TOC entry 271 (class 1259 OID 279504)
-- Name: notifications_notificationid_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

CREATE SEQUENCE public.notifications_notificationid_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE public.notifications_notificationid_seq OWNER TO postgres;

--
-- TOC entry 5807 (class 0 OID 0)
-- Dependencies: 271
-- Name: notifications_notificationid_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: postgres
--

ALTER SEQUENCE public.notifications_notificationid_seq OWNED BY public.notifications.notificationid;


--
-- TOC entry 272 (class 1259 OID 279505)
-- Name: profilesuspensions; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.profilesuspensions (
    suspensionid integer NOT NULL,
    userid character varying(50),
    suspensiontype character varying(30) NOT NULL,
    reason text,
    startdate timestamp without time zone DEFAULT CURRENT_TIMESTAMP,
    enddate timestamp without time zone,
    isactive boolean DEFAULT true,
    createdby character varying(50)
);


ALTER TABLE public.profilesuspensions OWNER TO postgres;

--
-- TOC entry 273 (class 1259 OID 279512)
-- Name: profilesuspensions_suspensionid_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

CREATE SEQUENCE public.profilesuspensions_suspensionid_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE public.profilesuspensions_suspensionid_seq OWNER TO postgres;

--
-- TOC entry 5808 (class 0 OID 0)
-- Dependencies: 273
-- Name: profilesuspensions_suspensionid_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: postgres
--

ALTER SEQUENCE public.profilesuspensions_suspensionid_seq OWNED BY public.profilesuspensions.suspensionid;


--
-- TOC entry 274 (class 1259 OID 279513)
-- Name: promotions; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.promotions (
    promotionid integer NOT NULL,
    code character varying(50) NOT NULL,
    description text,
    discounttype character varying(20),
    discountvalue numeric(12,2),
    maxdiscountamount numeric(12,2),
    minordervalue numeric(12,2),
    startdate timestamp without time zone,
    enddate timestamp without time zone,
    usagelimit integer,
    usagecount integer DEFAULT 0,
    isactive boolean DEFAULT true,
    createdat timestamp without time zone DEFAULT CURRENT_TIMESTAMP
);


ALTER TABLE public.promotions OWNER TO postgres;

--
-- TOC entry 275 (class 1259 OID 279521)
-- Name: promotions_promotionid_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

CREATE SEQUENCE public.promotions_promotionid_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE public.promotions_promotionid_seq OWNER TO postgres;

--
-- TOC entry 5809 (class 0 OID 0)
-- Dependencies: 275
-- Name: promotions_promotionid_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: postgres
--

ALTER SEQUENCE public.promotions_promotionid_seq OWNED BY public.promotions.promotionid;


--
-- TOC entry 304 (class 1259 OID 280226)
-- Name: refreshtokens; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.refreshtokens (
    id character varying(50) NOT NULL,
    tokenhash character varying(128) NOT NULL,
    userid character varying(50) NOT NULL,
    tokenfamily character varying(50) NOT NULL,
    expiresat timestamp without time zone NOT NULL,
    createdat timestamp without time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    revokedat timestamp without time zone,
    replacedbytokenhash character varying(128)
);


ALTER TABLE public.refreshtokens OWNER TO postgres;

--
-- TOC entry 5810 (class 0 OID 0)
-- Dependencies: 304
-- Name: TABLE refreshtokens; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON TABLE public.refreshtokens IS 'Refresh tokens with rotation support. Raw tokens are never stored - only SHA256 hashes.';


--
-- TOC entry 5811 (class 0 OID 0)
-- Dependencies: 304
-- Name: COLUMN refreshtokens.tokenhash; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public.refreshtokens.tokenhash IS 'SHA256 hash of the raw refresh token (hex string)';


--
-- TOC entry 5812 (class 0 OID 0)
-- Dependencies: 304
-- Name: COLUMN refreshtokens.tokenfamily; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public.refreshtokens.tokenfamily IS 'GUID shared by all rotated tokens from the same login session. Used to detect reuse attacks.';


--
-- TOC entry 5813 (class 0 OID 0)
-- Dependencies: 304
-- Name: COLUMN refreshtokens.revokedat; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public.refreshtokens.revokedat IS 'Set when token is rotated or explicitly revoked. NULL = still valid.';


--
-- TOC entry 5814 (class 0 OID 0)
-- Dependencies: 304
-- Name: COLUMN refreshtokens.replacedbytokenhash; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public.refreshtokens.replacedbytokenhash IS 'Hash of the successor token after rotation (for audit trail).';


--
-- TOC entry 276 (class 1259 OID 279522)
-- Name: studentgrades; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.studentgrades (
    gradeid integer NOT NULL,
    studentid character varying(50),
    bookingid integer,
    tutorid character varying(50),
    subjectid integer,
    examname character varying(200),
    examtype character varying(50),
    score numeric(5,2),
    maxscore numeric(5,2) DEFAULT 10,
    examdate date,
    notes text,
    createdat timestamp without time zone DEFAULT CURRENT_TIMESTAMP
);


ALTER TABLE public.studentgrades OWNER TO postgres;

--
-- TOC entry 277 (class 1259 OID 279529)
-- Name: studentgrades_gradeid_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

CREATE SEQUENCE public.studentgrades_gradeid_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE public.studentgrades_gradeid_seq OWNER TO postgres;

--
-- TOC entry 5815 (class 0 OID 0)
-- Dependencies: 277
-- Name: studentgrades_gradeid_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: postgres
--

ALTER SEQUENCE public.studentgrades_gradeid_seq OWNED BY public.studentgrades.gradeid;


--
-- TOC entry 278 (class 1259 OID 279530)
-- Name: studentprofiles; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.studentprofiles (
    studentid character varying(50) NOT NULL,
    parentid character varying(50),
    linkeduserid character varying(50),
    fullname character varying(100),
    birthdate date,
    school character varying(255),
    learninggoals text,
    avatarurl character varying(1000),
    createdat timestamp without time zone DEFAULT CURRENT_TIMESTAMP,
    studentcode character varying(20),
    studentcodeexpiresat timestamp without time zone,
    deletedat timestamp without time zone,
    gradelevelid integer
);


ALTER TABLE public.studentprofiles OWNER TO postgres;

--
-- TOC entry 279 (class 1259 OID 279536)
-- Name: subjects; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.subjects (
    subjectid integer NOT NULL,
    subjectname character varying(100),
    description text
);


ALTER TABLE public.subjects OWNER TO postgres;

--
-- TOC entry 280 (class 1259 OID 279541)
-- Name: subjects_subjectid_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

CREATE SEQUENCE public.subjects_subjectid_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE public.subjects_subjectid_seq OWNER TO postgres;

--
-- TOC entry 5816 (class 0 OID 0)
-- Dependencies: 280
-- Name: subjects_subjectid_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: postgres
--

ALTER SEQUENCE public.subjects_subjectid_seq OWNED BY public.subjects.subjectid;


--
-- TOC entry 281 (class 1259 OID 279542)
-- Name: systemalerts; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.systemalerts (
    alertid integer NOT NULL,
    type character varying(50) NOT NULL,
    severity character varying(20) NOT NULL,
    message text NOT NULL,
    metadata jsonb,
    resolved boolean DEFAULT false,
    resolvedat timestamp without time zone,
    createdat timestamp without time zone DEFAULT CURRENT_TIMESTAMP,
    resolvedby character varying(50)
);


ALTER TABLE public.systemalerts OWNER TO postgres;

--
-- TOC entry 5817 (class 0 OID 0)
-- Dependencies: 281
-- Name: TABLE systemalerts; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON TABLE public.systemalerts IS 'System-level alerts for admin monitoring';


--
-- TOC entry 5818 (class 0 OID 0)
-- Dependencies: 281
-- Name: COLUMN systemalerts.type; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public.systemalerts.type IS 'Alert type: LOW_BALANCE, CRITICAL_BALANCE, PAYOUT_FAILED, etc.';


--
-- TOC entry 5819 (class 0 OID 0)
-- Dependencies: 281
-- Name: COLUMN systemalerts.severity; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public.systemalerts.severity IS 'Alert severity: info, warning, critical';


--
-- TOC entry 5820 (class 0 OID 0)
-- Dependencies: 281
-- Name: COLUMN systemalerts.message; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public.systemalerts.message IS 'Human-readable alert message in English';


--
-- TOC entry 5821 (class 0 OID 0)
-- Dependencies: 281
-- Name: COLUMN systemalerts.metadata; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public.systemalerts.metadata IS 'Additional context data in JSON format';


--
-- TOC entry 5822 (class 0 OID 0)
-- Dependencies: 281
-- Name: COLUMN systemalerts.resolved; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public.systemalerts.resolved IS 'Whether the alert has been resolved';


--
-- TOC entry 5823 (class 0 OID 0)
-- Dependencies: 281
-- Name: COLUMN systemalerts.resolvedat; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public.systemalerts.resolvedat IS 'Timestamp when alert was resolved';


--
-- TOC entry 5824 (class 0 OID 0)
-- Dependencies: 281
-- Name: COLUMN systemalerts.resolvedby; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public.systemalerts.resolvedby IS 'Admin user ID who resolved the alert';


--
-- TOC entry 282 (class 1259 OID 279549)
-- Name: systemalerts_alertid_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

CREATE SEQUENCE public.systemalerts_alertid_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE public.systemalerts_alertid_seq OWNER TO postgres;

--
-- TOC entry 5825 (class 0 OID 0)
-- Dependencies: 282
-- Name: systemalerts_alertid_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: postgres
--

ALTER SEQUENCE public.systemalerts_alertid_seq OWNED BY public.systemalerts.alertid;


--
-- TOC entry 283 (class 1259 OID 279550)
-- Name: systemconfigs; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.systemconfigs (
    configid integer NOT NULL,
    configkey character varying(100) NOT NULL,
    configvalue text,
    description text,
    updatedby character varying(50),
    updatedat timestamp without time zone DEFAULT CURRENT_TIMESTAMP
);


ALTER TABLE public.systemconfigs OWNER TO postgres;

--
-- TOC entry 284 (class 1259 OID 279556)
-- Name: systemconfigs_configid_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

CREATE SEQUENCE public.systemconfigs_configid_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE public.systemconfigs_configid_seq OWNER TO postgres;

--
-- TOC entry 5826 (class 0 OID 0)
-- Dependencies: 284
-- Name: systemconfigs_configid_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: postgres
--

ALTER SEQUENCE public.systemconfigs_configid_seq OWNED BY public.systemconfigs.configid;


--
-- TOC entry 285 (class 1259 OID 279557)
-- Name: topuprequests; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.topuprequests (
    topuprequestid integer NOT NULL,
    ordercode bigint NOT NULL,
    userid character varying(50),
    amount numeric(15,2),
    status character varying(20) DEFAULT 'pending'::character varying,
    paymentlinkid character varying(255),
    createdat timestamp without time zone DEFAULT CURRENT_TIMESTAMP,
    completedat timestamp without time zone,
    expiresat timestamp without time zone
);


ALTER TABLE public.topuprequests OWNER TO postgres;

--
-- TOC entry 286 (class 1259 OID 279562)
-- Name: topuprequests_topuprequestid_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

CREATE SEQUENCE public.topuprequests_topuprequestid_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE public.topuprequests_topuprequestid_seq OWNER TO postgres;

--
-- TOC entry 5827 (class 0 OID 0)
-- Dependencies: 286
-- Name: topuprequests_topuprequestid_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: postgres
--

ALTER SEQUENCE public.topuprequests_topuprequestid_seq OWNED BY public.topuprequests.topuprequestid;


--
-- TOC entry 287 (class 1259 OID 279563)
-- Name: tutoravailability; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.tutoravailability (
    availabilityid integer NOT NULL,
    tutorid character varying(50),
    dayofweek integer,
    starttime time without time zone,
    endtime time without time zone,
    createdat timestamp without time zone DEFAULT CURRENT_TIMESTAMP,
    isactive boolean DEFAULT true NOT NULL,
    CONSTRAINT ck_tutoravailability_dayofweek CHECK (((dayofweek >= 1) AND (dayofweek <= 7))),
    CONSTRAINT ck_tutoravailability_time_range CHECK ((starttime < endtime))
);


ALTER TABLE public.tutoravailability OWNER TO postgres;

--
-- TOC entry 288 (class 1259 OID 279567)
-- Name: tutoravailability_availabilityid_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

CREATE SEQUENCE public.tutoravailability_availabilityid_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE public.tutoravailability_availabilityid_seq OWNER TO postgres;

--
-- TOC entry 5828 (class 0 OID 0)
-- Dependencies: 288
-- Name: tutoravailability_availabilityid_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: postgres
--

ALTER SEQUENCE public.tutoravailability_availabilityid_seq OWNED BY public.tutoravailability.availabilityid;


--
-- TOC entry 289 (class 1259 OID 279568)
-- Name: tutorcertificates; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.tutorcertificates (
    certificateid character varying(36) DEFAULT gen_random_uuid() NOT NULL,
    tutorid character varying(36) NOT NULL,
    certificatename character varying(200) NOT NULL,
    certificatetype character varying(50) NOT NULL,
    issuingorganization character varying(200) NOT NULL,
    yearissued integer,
    credentialid character varying(100),
    credentialurl character varying(2000),
    certificatefileurl character varying(2000) NOT NULL,
    createdat timestamp without time zone DEFAULT CURRENT_TIMESTAMP,
    updatedat timestamp without time zone DEFAULT CURRENT_TIMESTAMP,
    verificationstatus character varying(20) DEFAULT 'pending'::character varying,
    verificationnote text
);


ALTER TABLE public.tutorcertificates OWNER TO postgres;

--
-- TOC entry 312 (class 1259 OID 295625)
-- Name: tutorpackagefixedslots; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.tutorpackagefixedslots (
    fixedslotid integer NOT NULL,
    packageid integer NOT NULL,
    dayofweek integer NOT NULL,
    starttime time without time zone NOT NULL,
    endtime time without time zone NOT NULL,
    createdat timestamp without time zone DEFAULT now(),
    CONSTRAINT ck_tutorpackagefixedslots_dayofweek CHECK (((dayofweek >= 1) AND (dayofweek <= 7))),
    CONSTRAINT ck_tutorpackagefixedslots_time_range CHECK ((starttime < endtime))
);


ALTER TABLE public.tutorpackagefixedslots OWNER TO postgres;

--
-- TOC entry 5829 (class 0 OID 0)
-- Dependencies: 312
-- Name: TABLE tutorpackagefixedslots; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON TABLE public.tutorpackagefixedslots IS 'Lich co dinh cua package fixed. Package flexible khong co ban ghi trong bang nay.';


--
-- TOC entry 311 (class 1259 OID 295624)
-- Name: tutorpackagefixedslots_fixedslotid_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

ALTER TABLE public.tutorpackagefixedslots ALTER COLUMN fixedslotid ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public.tutorpackagefixedslots_fixedslotid_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- TOC entry 310 (class 1259 OID 295606)
-- Name: tutorpackages; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.tutorpackages (
    packageid integer NOT NULL,
    tutorid character varying(50) NOT NULL,
    name character varying(255) NOT NULL,
    packagetype integer NOT NULL,
    durationminutespersession integer NOT NULL,
    description text,
    isactive boolean DEFAULT true NOT NULL,
    createdat timestamp without time zone DEFAULT now(),
    updatedat timestamp without time zone DEFAULT now(),
    CONSTRAINT ck_tutorpackages_packagetype CHECK ((packagetype = ANY (ARRAY[1, 2]))),
    CONSTRAINT ck_tutorpackages_duration_positive CHECK ((durationminutespersession > 0))
);


ALTER TABLE public.tutorpackages OWNER TO postgres;

--
-- TOC entry 5830 (class 0 OID 0)
-- Dependencies: 310
-- Name: TABLE tutorpackages; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON TABLE public.tutorpackages IS 'Package hoc do tutor tao. packagetype=1 la flexible, packagetype=2 la fixed. Booking moi tham chieu package nay qua bookings.packageid.';


--
-- TOC entry 309 (class 1259 OID 295605)
-- Name: tutorpackages_packageid_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

ALTER TABLE public.tutorpackages ALTER COLUMN packageid ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public.tutorpackages_packageid_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- TOC entry 290 (class 1259 OID 279577)
-- Name: tutorprofiles; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.tutorprofiles (
    tutorid character varying(50) NOT NULL,
    bio text,
    headline character varying(200),
    videointrourl character varying(1000),
    education character varying(255),
    gpascale double precision,
    gpa double precision,
    experience text,
    teachingareacity character varying(50),
    teachingareadistrict character varying(50),
    profilestatus character varying(30) DEFAULT 'draft'::character varying,
    ispublic boolean DEFAULT false,
    averagerating double precision DEFAULT 0.0,
    totalreviews integer DEFAULT 0,
    completedhours integer DEFAULT 0,
    createdat timestamp without time zone DEFAULT CURRENT_TIMESTAMP,
    updatedat timestamp without time zone DEFAULT CURRENT_TIMESTAMP,
    rejectionnote text,
    subscriptiontype character varying(30) DEFAULT 'free'::character varying,
    triallessonprice numeric(12,2),
    allowpricenegotiation boolean DEFAULT false,
    deletedat timestamp without time zone,
    reviewedby character varying(255),
    reviewedat timestamp without time zone,
    bankname character varying(100),
    bankaccountnumber character varying(50),
    bankaccountname character varying(100),
    isbankverified boolean DEFAULT false,
    bankchangedat timestamp without time zone,
    bankverifiedat timestamp without time zone,
    bankverifyattempts integer DEFAULT 0,
    bankverifycode character varying(50),
    bankverifyrequested timestamp without time zone,
    bankverifystatus character varying(30)
);


ALTER TABLE public.tutorprofiles OWNER TO postgres;

--
-- TOC entry 5831 (class 0 OID 0)
-- Dependencies: 290
-- Name: COLUMN tutorprofiles.triallessonprice; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public.tutorprofiles.triallessonprice IS 'Trial lesson price in VND.';


--
-- TOC entry 5832 (class 0 OID 0)
-- Dependencies: 290
-- Name: COLUMN tutorprofiles.allowpricenegotiation; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public.tutorprofiles.allowpricenegotiation IS 'Whether the tutor allows price negotiation';


--
-- TOC entry 5833 (class 0 OID 0)
-- Dependencies: 290
-- Name: COLUMN tutorprofiles.bankname; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public.tutorprofiles.bankname IS 'Name of the bank for tutor withdrawals';


--
-- TOC entry 5834 (class 0 OID 0)
-- Dependencies: 290
-- Name: COLUMN tutorprofiles.bankaccountnumber; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public.tutorprofiles.bankaccountnumber IS 'Bank account number (8-19 digits)';


--
-- TOC entry 5835 (class 0 OID 0)
-- Dependencies: 290
-- Name: COLUMN tutorprofiles.bankaccountname; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public.tutorprofiles.bankaccountname IS 'Account holder name (uppercase, no accents)';


--
-- TOC entry 5836 (class 0 OID 0)
-- Dependencies: 290
-- Name: COLUMN tutorprofiles.isbankverified; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public.tutorprofiles.isbankverified IS 'Whether the bank account has been verified by admin';


--
-- TOC entry 5837 (class 0 OID 0)
-- Dependencies: 290
-- Name: COLUMN tutorprofiles.bankchangedat; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public.tutorprofiles.bankchangedat IS 'Timestamp when bank info was last updated';


--
-- TOC entry 308 (class 1259 OID 295578)
-- Name: tutorsubjectgradeprices; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.tutorsubjectgradeprices (
    id integer NOT NULL,
    tutorid character varying(50) NOT NULL,
    subjectid integer NOT NULL,
    gradelevelid integer NOT NULL,
    priceperhour numeric(12,2) NOT NULL,
    currency character varying(10) DEFAULT 'VND'::character varying NOT NULL,
    isactive boolean DEFAULT true NOT NULL,
    createdat timestamp without time zone DEFAULT now(),
    updatedat timestamp without time zone DEFAULT now(),
    CONSTRAINT ck_tutorsubjectgradeprices_priceperhour_nonnegative CHECK ((priceperhour >= (0)::numeric))
);


ALTER TABLE public.tutorsubjectgradeprices OWNER TO postgres;

--
-- TOC entry 5838 (class 0 OID 0)
-- Dependencies: 308
-- Name: TABLE tutorsubjectgradeprices; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON TABLE public.tutorsubjectgradeprices IS 'Tutor day mon nao, khoi nao, gia bao nhieu. Day la source of truth moi cho subject + grade + pricing.';


--
-- TOC entry 307 (class 1259 OID 295577)
-- Name: tutorsubjectgradeprices_id_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

ALTER TABLE public.tutorsubjectgradeprices ALTER COLUMN id ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public.tutorsubjectgradeprices_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- TOC entry 291 (class 1259 OID 279600)
-- Name: tutorsubscriptions; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.tutorsubscriptions (
    subscriptionid integer NOT NULL,
    tutorid character varying(50),
    packagetype character varying(30) NOT NULL,
    startdate timestamp without time zone DEFAULT CURRENT_TIMESTAMP,
    enddate timestamp without time zone,
    price numeric(12,2),
    paymentstatus character varying(20) DEFAULT 'pending'::character varying,
    isactive boolean DEFAULT true
);


ALTER TABLE public.tutorsubscriptions OWNER TO postgres;

--
-- TOC entry 292 (class 1259 OID 279606)
-- Name: tutorsubscriptions_subscriptionid_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

CREATE SEQUENCE public.tutorsubscriptions_subscriptionid_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE public.tutorsubscriptions_subscriptionid_seq OWNER TO postgres;

--
-- TOC entry 5839 (class 0 OID 0)
-- Dependencies: 292
-- Name: tutorsubscriptions_subscriptionid_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: postgres
--

ALTER SEQUENCE public.tutorsubscriptions_subscriptionid_seq OWNED BY public.tutorsubscriptions.subscriptionid;


--
-- TOC entry 293 (class 1259 OID 279607)
-- Name: users; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.users (
    userid character varying(50) NOT NULL,
    username character varying(50),
    password character varying(255) NOT NULL,
    email character varying(100) NOT NULL,
    phone character varying(20),
    isphoneverified boolean DEFAULT false,
    zalouserid character varying(100),
    fullname character varying(100),
    birthdate date,
    gender character varying(10),
    avatarurl character varying(1000),
    address character varying(255),
    identitynumber character varying(50),
    idcardfronturl character varying(1000),
    idcardbackurl character varying(1000),
    isidentityverified boolean DEFAULT false,
    status integer DEFAULT 1,
    isemailverified boolean DEFAULT false,
    lastloginat timestamp without time zone,
    createdat timestamp without time zone DEFAULT CURRENT_TIMESTAMP,
    ekycrawdata text,
    otpcode character varying(10),
    otpexpiresat timestamp without time zone,
    otpattempts integer DEFAULT 0,
    primaryrole character varying(20),
    googlecalendartoken text,
    zabornotifyenabled boolean DEFAULT true,
    parentcode character varying(10),
    parentcodeexpiresat timestamp without time zone,
    hascompletedtour boolean DEFAULT false,
    fcmtoken character varying(500),
    isdeactivated boolean DEFAULT false,
    deactivatedat timestamp without time zone
);


ALTER TABLE public.users OWNER TO postgres;

--
-- TOC entry 294 (class 1259 OID 279620)
-- Name: userwarnings; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.userwarnings (
    warningid integer NOT NULL,
    userid character varying(50),
    warninglevel integer NOT NULL,
    reason text,
    relatedbookingid integer,
    issuedby character varying(50),
    createdat timestamp without time zone DEFAULT CURRENT_TIMESTAMP
);


ALTER TABLE public.userwarnings OWNER TO postgres;

--
-- TOC entry 295 (class 1259 OID 279626)
-- Name: userwarnings_warningid_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

CREATE SEQUENCE public.userwarnings_warningid_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE public.userwarnings_warningid_seq OWNER TO postgres;

--
-- TOC entry 5840 (class 0 OID 0)
-- Dependencies: 295
-- Name: userwarnings_warningid_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: postgres
--

ALTER SEQUENCE public.userwarnings_warningid_seq OWNED BY public.userwarnings.warningid;


--
-- TOC entry 296 (class 1259 OID 279627)
-- Name: wallets; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.wallets (
    walletid integer NOT NULL,
    userid character varying(50),
    balance numeric(15,2) DEFAULT 0,
    frozenbalance numeric(15,2) DEFAULT 0,
    lastupdated timestamp without time zone DEFAULT CURRENT_TIMESTAMP
);


ALTER TABLE public.wallets OWNER TO postgres;

--
-- TOC entry 297 (class 1259 OID 279633)
-- Name: wallets_walletid_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

CREATE SEQUENCE public.wallets_walletid_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE public.wallets_walletid_seq OWNER TO postgres;

--
-- TOC entry 5841 (class 0 OID 0)
-- Dependencies: 297
-- Name: wallets_walletid_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: postgres
--

ALTER SEQUENCE public.wallets_walletid_seq OWNED BY public.wallets.walletid;


--
-- TOC entry 298 (class 1259 OID 279634)
-- Name: wallettransactions; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.wallettransactions (
    transactionid integer NOT NULL,
    walletid integer,
    amount numeric(15,2),
    transactiontype character varying(50),
    referenceid integer,
    referencetable character varying(50),
    description text,
    createdat timestamp without time zone DEFAULT CURRENT_TIMESTAMP,
    ordercode bigint
);


ALTER TABLE public.wallettransactions OWNER TO postgres;

--
-- TOC entry 299 (class 1259 OID 279640)
-- Name: wallettransactions_transactionid_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

CREATE SEQUENCE public.wallettransactions_transactionid_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE public.wallettransactions_transactionid_seq OWNER TO postgres;

--
-- TOC entry 5842 (class 0 OID 0)
-- Dependencies: 299
-- Name: wallettransactions_transactionid_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: postgres
--

ALTER SEQUENCE public.wallettransactions_transactionid_seq OWNED BY public.wallettransactions.transactionid;


--
-- TOC entry 300 (class 1259 OID 279641)
-- Name: withdrawal_scores; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.withdrawal_scores (
    scoreid integer NOT NULL,
    withdrawalrequestid integer NOT NULL,
    tutorid character varying(50) NOT NULL,
    basescore integer DEFAULT 50 NOT NULL,
    positivefactors jsonb,
    negativefactors jsonb,
    fraudflags jsonb,
    totalscore integer NOT NULL,
    decision character varying(50) NOT NULL,
    createdat timestamp without time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    CONSTRAINT chk_decision_valid CHECK (((decision)::text = ANY (ARRAY[('AUTO_APPROVE'::character varying)::text, ('DELAYED'::character varying)::text, ('MANUAL_REVIEW'::character varying)::text, ('AUTO_REJECT'::character varying)::text]))),
    CONSTRAINT chk_totalscore_range CHECK (((totalscore >= 0) AND (totalscore <= 100)))
);


ALTER TABLE public.withdrawal_scores OWNER TO postgres;

--
-- TOC entry 5843 (class 0 OID 0)
-- Dependencies: 300
-- Name: TABLE withdrawal_scores; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON TABLE public.withdrawal_scores IS 'Audit trail for trust score calculations and approval decisions';


--
-- TOC entry 5844 (class 0 OID 0)
-- Dependencies: 300
-- Name: COLUMN withdrawal_scores.basescore; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public.withdrawal_scores.basescore IS 'Starting score (default 50)';


--
-- TOC entry 5845 (class 0 OID 0)
-- Dependencies: 300
-- Name: COLUMN withdrawal_scores.positivefactors; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public.withdrawal_scores.positivefactors IS 'Array of positive factors: [{ factor, points, detail }]';


--
-- TOC entry 5846 (class 0 OID 0)
-- Dependencies: 300
-- Name: COLUMN withdrawal_scores.negativefactors; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public.withdrawal_scores.negativefactors IS 'Array of negative factors: [{ factor, points, detail }]';


--
-- TOC entry 5847 (class 0 OID 0)
-- Dependencies: 300
-- Name: COLUMN withdrawal_scores.fraudflags; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public.withdrawal_scores.fraudflags IS 'Array of fraud flags from fraud detection engine';


--
-- TOC entry 5848 (class 0 OID 0)
-- Dependencies: 300
-- Name: COLUMN withdrawal_scores.totalscore; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public.withdrawal_scores.totalscore IS 'Final score after applying all factors (clamped 0-100)';


--
-- TOC entry 5849 (class 0 OID 0)
-- Dependencies: 300
-- Name: COLUMN withdrawal_scores.decision; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public.withdrawal_scores.decision IS 'Approval decision: AUTO_APPROVE, DELAYED, MANUAL_REVIEW, AUTO_REJECT';


--
-- TOC entry 301 (class 1259 OID 279650)
-- Name: withdrawal_scores_scoreid_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

CREATE SEQUENCE public.withdrawal_scores_scoreid_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE public.withdrawal_scores_scoreid_seq OWNER TO postgres;

--
-- TOC entry 5850 (class 0 OID 0)
-- Dependencies: 301
-- Name: withdrawal_scores_scoreid_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: postgres
--

ALTER SEQUENCE public.withdrawal_scores_scoreid_seq OWNED BY public.withdrawal_scores.scoreid;


--
-- TOC entry 302 (class 1259 OID 279651)
-- Name: withdrawalrequests; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.withdrawalrequests (
    withdrawalid integer NOT NULL,
    userid character varying(50),
    amount numeric(15,2),
    bankname character varying(100),
    accountnumber character varying(50),
    accountholdername character varying(100),
    status character varying(20),
    requestedat timestamp without time zone DEFAULT CURRENT_TIMESTAMP,
    processedat timestamp without time zone,
    payostransactionid character varying(255),
    payosstatus character varying(50),
    payosresponsecode character varying(50),
    payoserror text,
    retrycount integer DEFAULT 0,
    lastretryat timestamp without time zone,
    decision character varying(50),
    processedby character varying(50)
);


ALTER TABLE public.withdrawalrequests OWNER TO postgres;

--
-- TOC entry 5851 (class 0 OID 0)
-- Dependencies: 302
-- Name: COLUMN withdrawalrequests.payostransactionid; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public.withdrawalrequests.payostransactionid IS 'PayOS payout transaction ID for tracking';


--
-- TOC entry 5852 (class 0 OID 0)
-- Dependencies: 302
-- Name: COLUMN withdrawalrequests.payosstatus; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public.withdrawalrequests.payosstatus IS 'PayOS payout status: PENDING, SUCCESS, FAILED';


--
-- TOC entry 5853 (class 0 OID 0)
-- Dependencies: 302
-- Name: COLUMN withdrawalrequests.payosresponsecode; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public.withdrawalrequests.payosresponsecode IS 'PayOS error code if payout failed';


--
-- TOC entry 5854 (class 0 OID 0)
-- Dependencies: 302
-- Name: COLUMN withdrawalrequests.payoserror; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public.withdrawalrequests.payoserror IS 'PayOS error message in English';


--
-- TOC entry 5855 (class 0 OID 0)
-- Dependencies: 302
-- Name: COLUMN withdrawalrequests.retrycount; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public.withdrawalrequests.retrycount IS 'Number of retry attempts for failed payouts';


--
-- TOC entry 5856 (class 0 OID 0)
-- Dependencies: 302
-- Name: COLUMN withdrawalrequests.lastretryat; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public.withdrawalrequests.lastretryat IS 'Timestamp of last retry attempt';


--
-- TOC entry 5857 (class 0 OID 0)
-- Dependencies: 302
-- Name: COLUMN withdrawalrequests.decision; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public.withdrawalrequests.decision IS 'Trust scoring decision: AUTO_APPROVE, DELAYED, MANUAL_REVIEW';


--
-- TOC entry 5858 (class 0 OID 0)
-- Dependencies: 302
-- Name: COLUMN withdrawalrequests.processedby; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public.withdrawalrequests.processedby IS 'Who processed the withdrawal: SYSTEM or admin username';


--
-- TOC entry 303 (class 1259 OID 279658)
-- Name: withdrawalrequests_withdrawalid_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

CREATE SEQUENCE public.withdrawalrequests_withdrawalid_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE public.withdrawalrequests_withdrawalid_seq OWNER TO postgres;

--
-- TOC entry 5859 (class 0 OID 0)
-- Dependencies: 303
-- Name: withdrawalrequests_withdrawalid_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: postgres
--

ALTER SEQUENCE public.withdrawalrequests_withdrawalid_seq OWNED BY public.withdrawalrequests.withdrawalid;


--
-- TOC entry 5076 (class 2604 OID 279659)
-- Name: aggregatedcounter id; Type: DEFAULT; Schema: hangfire; Owner: postgres
--

ALTER TABLE ONLY hangfire.aggregatedcounter ALTER COLUMN id SET DEFAULT nextval('hangfire.aggregatedcounter_id_seq'::regclass);


--
-- TOC entry 5077 (class 2604 OID 279660)
-- Name: counter id; Type: DEFAULT; Schema: hangfire; Owner: postgres
--

ALTER TABLE ONLY hangfire.counter ALTER COLUMN id SET DEFAULT nextval('hangfire.counter_id_seq'::regclass);


--
-- TOC entry 5078 (class 2604 OID 279661)
-- Name: hash id; Type: DEFAULT; Schema: hangfire; Owner: postgres
--

ALTER TABLE ONLY hangfire.hash ALTER COLUMN id SET DEFAULT nextval('hangfire.hash_id_seq'::regclass);


--
-- TOC entry 5080 (class 2604 OID 279662)
-- Name: job id; Type: DEFAULT; Schema: hangfire; Owner: postgres
--

ALTER TABLE ONLY hangfire.job ALTER COLUMN id SET DEFAULT nextval('hangfire.job_id_seq'::regclass);


--
-- TOC entry 5082 (class 2604 OID 279663)
-- Name: jobparameter id; Type: DEFAULT; Schema: hangfire; Owner: postgres
--

ALTER TABLE ONLY hangfire.jobparameter ALTER COLUMN id SET DEFAULT nextval('hangfire.jobparameter_id_seq'::regclass);


--
-- TOC entry 5084 (class 2604 OID 279664)
-- Name: jobqueue id; Type: DEFAULT; Schema: hangfire; Owner: postgres
--

ALTER TABLE ONLY hangfire.jobqueue ALTER COLUMN id SET DEFAULT nextval('hangfire.jobqueue_id_seq'::regclass);


--
-- TOC entry 5086 (class 2604 OID 279665)
-- Name: list id; Type: DEFAULT; Schema: hangfire; Owner: postgres
--

ALTER TABLE ONLY hangfire.list ALTER COLUMN id SET DEFAULT nextval('hangfire.list_id_seq'::regclass);


--
-- TOC entry 5090 (class 2604 OID 279666)
-- Name: set id; Type: DEFAULT; Schema: hangfire; Owner: postgres
--

ALTER TABLE ONLY hangfire.set ALTER COLUMN id SET DEFAULT nextval('hangfire.set_id_seq'::regclass);


--
-- TOC entry 5092 (class 2604 OID 279667)
-- Name: state id; Type: DEFAULT; Schema: hangfire; Owner: postgres
--

ALTER TABLE ONLY hangfire.state ALTER COLUMN id SET DEFAULT nextval('hangfire.state_id_seq'::regclass);


--
-- TOC entry 5094 (class 2604 OID 279668)
-- Name: bank_change_logs logid; Type: DEFAULT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.bank_change_logs ALTER COLUMN logid SET DEFAULT nextval('public.bank_change_logs_logid_seq'::regclass);


--
-- TOC entry 5095 (class 2604 OID 279669)
-- Name: bookings bookingid; Type: DEFAULT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.bookings ALTER COLUMN bookingid SET DEFAULT nextval('public.bookings_bookingid_seq'::regclass);


--
-- TOC entry 5100 (class 2604 OID 279670)
-- Name: chatchannels channelid; Type: DEFAULT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.chatchannels ALTER COLUMN channelid SET DEFAULT nextval('public.chatchannels_channelid_seq'::regclass);


--
-- TOC entry 5103 (class 2604 OID 279671)
-- Name: chatmessages messageid; Type: DEFAULT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.chatmessages ALTER COLUMN messageid SET DEFAULT nextval('public.chatmessages_messageid_seq'::regclass);


--
-- TOC entry 5107 (class 2604 OID 279672)
-- Name: classes classid; Type: DEFAULT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.classes ALTER COLUMN classid SET DEFAULT nextval('public.classes_classid_seq'::regclass);


--
-- TOC entry 5110 (class 2604 OID 279673)
-- Name: disputes disputeid; Type: DEFAULT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.disputes ALTER COLUMN disputeid SET DEFAULT nextval('public.disputes_disputeid_seq'::regclass);


--
-- TOC entry 5113 (class 2604 OID 279674)
-- Name: feedbacks feedbackid; Type: DEFAULT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.feedbacks ALTER COLUMN feedbackid SET DEFAULT nextval('public.feedbacks_feedbackid_seq'::regclass);


--
-- TOC entry 5117 (class 2604 OID 279675)
-- Name: fraud_logs logid; Type: DEFAULT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.fraud_logs ALTER COLUMN logid SET DEFAULT nextval('public.fraud_logs_logid_seq'::regclass);


--
-- TOC entry 5121 (class 2604 OID 279676)
-- Name: handoversummaries summaryid; Type: DEFAULT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.handoversummaries ALTER COLUMN summaryid SET DEFAULT nextval('public.handoversummaries_summaryid_seq'::regclass);


--
-- TOC entry 5123 (class 2604 OID 279677)
-- Name: learningmaterials materialid; Type: DEFAULT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.learningmaterials ALTER COLUMN materialid SET DEFAULT nextval('public.learningmaterials_materialid_seq'::regclass);


--
-- TOC entry 5126 (class 2604 OID 279678)
-- Name: lessonreports reportid; Type: DEFAULT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.lessonreports ALTER COLUMN reportid SET DEFAULT nextval('public.lessonreports_reportid_seq'::regclass);


--
-- TOC entry 5128 (class 2604 OID 279679)
-- Name: lessons lessonid; Type: DEFAULT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.lessons ALTER COLUMN lessonid SET DEFAULT nextval('public.lessons_lessonid_seq'::regclass);


--
-- TOC entry 5134 (class 2604 OID 279680)
-- Name: login_history logid; Type: DEFAULT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.login_history ALTER COLUMN logid SET DEFAULT nextval('public.login_history_logid_seq'::regclass);


--
-- TOC entry 5136 (class 2604 OID 279681)
-- Name: notifications notificationid; Type: DEFAULT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.notifications ALTER COLUMN notificationid SET DEFAULT nextval('public.notifications_notificationid_seq'::regclass);


--
-- TOC entry 5140 (class 2604 OID 279682)
-- Name: profilesuspensions suspensionid; Type: DEFAULT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.profilesuspensions ALTER COLUMN suspensionid SET DEFAULT nextval('public.profilesuspensions_suspensionid_seq'::regclass);


--
-- TOC entry 5143 (class 2604 OID 279683)
-- Name: promotions promotionid; Type: DEFAULT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.promotions ALTER COLUMN promotionid SET DEFAULT nextval('public.promotions_promotionid_seq'::regclass);


--
-- TOC entry 5147 (class 2604 OID 279684)
-- Name: studentgrades gradeid; Type: DEFAULT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.studentgrades ALTER COLUMN gradeid SET DEFAULT nextval('public.studentgrades_gradeid_seq'::regclass);


--
-- TOC entry 5151 (class 2604 OID 279685)
-- Name: subjects subjectid; Type: DEFAULT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.subjects ALTER COLUMN subjectid SET DEFAULT nextval('public.subjects_subjectid_seq'::regclass);


--
-- TOC entry 5152 (class 2604 OID 279686)
-- Name: systemalerts alertid; Type: DEFAULT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.systemalerts ALTER COLUMN alertid SET DEFAULT nextval('public.systemalerts_alertid_seq'::regclass);


--
-- TOC entry 5155 (class 2604 OID 279687)
-- Name: systemconfigs configid; Type: DEFAULT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.systemconfigs ALTER COLUMN configid SET DEFAULT nextval('public.systemconfigs_configid_seq'::regclass);


--
-- TOC entry 5157 (class 2604 OID 279688)
-- Name: topuprequests topuprequestid; Type: DEFAULT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.topuprequests ALTER COLUMN topuprequestid SET DEFAULT nextval('public.topuprequests_topuprequestid_seq'::regclass);


--
-- TOC entry 5160 (class 2604 OID 279689)
-- Name: tutoravailability availabilityid; Type: DEFAULT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.tutoravailability ALTER COLUMN availabilityid SET DEFAULT nextval('public.tutoravailability_availabilityid_seq'::regclass);


--
-- TOC entry 5178 (class 2604 OID 279691)
-- Name: tutorsubscriptions subscriptionid; Type: DEFAULT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.tutorsubscriptions ALTER COLUMN subscriptionid SET DEFAULT nextval('public.tutorsubscriptions_subscriptionid_seq'::regclass);


--
-- TOC entry 5191 (class 2604 OID 279692)
-- Name: userwarnings warningid; Type: DEFAULT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.userwarnings ALTER COLUMN warningid SET DEFAULT nextval('public.userwarnings_warningid_seq'::regclass);


--
-- TOC entry 5193 (class 2604 OID 279693)
-- Name: wallets walletid; Type: DEFAULT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.wallets ALTER COLUMN walletid SET DEFAULT nextval('public.wallets_walletid_seq'::regclass);


--
-- TOC entry 5197 (class 2604 OID 279694)
-- Name: wallettransactions transactionid; Type: DEFAULT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.wallettransactions ALTER COLUMN transactionid SET DEFAULT nextval('public.wallettransactions_transactionid_seq'::regclass);


--
-- TOC entry 5199 (class 2604 OID 279695)
-- Name: withdrawal_scores scoreid; Type: DEFAULT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.withdrawal_scores ALTER COLUMN scoreid SET DEFAULT nextval('public.withdrawal_scores_scoreid_seq'::regclass);


--
-- TOC entry 5202 (class 2604 OID 279696)
-- Name: withdrawalrequests withdrawalid; Type: DEFAULT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.withdrawalrequests ALTER COLUMN withdrawalid SET DEFAULT nextval('public.withdrawalrequests_withdrawalid_seq'::regclass);


--
-- TOC entry 5673 (class 0 OID 279311)
-- Dependencies: 221
-- Data for Name: aggregatedcounter; Type: TABLE DATA; Schema: hangfire; Owner: postgres
--

INSERT INTO hangfire.aggregatedcounter VALUES (3, 'stats:succeeded', 2, NULL);


--
-- TOC entry 5675 (class 0 OID 279317)
-- Dependencies: 223
-- Data for Name: counter; Type: TABLE DATA; Schema: hangfire; Owner: postgres
--



--
-- TOC entry 5677 (class 0 OID 279323)
-- Dependencies: 225
-- Data for Name: hash; Type: TABLE DATA; Schema: hangfire; Owner: postgres
--



--
-- TOC entry 5679 (class 0 OID 279330)
-- Dependencies: 227
-- Data for Name: job; Type: TABLE DATA; Schema: hangfire; Owner: postgres
--



--
-- TOC entry 5681 (class 0 OID 279337)
-- Dependencies: 229
-- Data for Name: jobparameter; Type: TABLE DATA; Schema: hangfire; Owner: postgres
--



--
-- TOC entry 5683 (class 0 OID 279344)
-- Dependencies: 231
-- Data for Name: jobqueue; Type: TABLE DATA; Schema: hangfire; Owner: postgres
--



--
-- TOC entry 5685 (class 0 OID 279351)
-- Dependencies: 233
-- Data for Name: list; Type: TABLE DATA; Schema: hangfire; Owner: postgres
--



--
-- TOC entry 5687 (class 0 OID 279358)
-- Dependencies: 235
-- Data for Name: lock; Type: TABLE DATA; Schema: hangfire; Owner: postgres
--



--
-- TOC entry 5688 (class 0 OID 279364)
-- Dependencies: 236
-- Data for Name: schema; Type: TABLE DATA; Schema: hangfire; Owner: postgres
--

INSERT INTO hangfire.schema VALUES (23);


--
-- TOC entry 5689 (class 0 OID 279367)
-- Dependencies: 237
-- Data for Name: server; Type: TABLE DATA; Schema: hangfire; Owner: postgres
--

INSERT INTO hangfire.server VALUES ('laptop-dmtb9ov5:7156:8dd0fe9a-61f5-4f82-b9a8-7a111839a9c4', '{"Queues": ["default", "payout"], "StartedAt": "2026-06-05T07:30:30.5911609Z", "WorkerCount": 2}', '2026-06-05 14:46:31.38157+07', 0);


--
-- TOC entry 5690 (class 0 OID 279373)
-- Dependencies: 238
-- Data for Name: set; Type: TABLE DATA; Schema: hangfire; Owner: postgres
--



--
-- TOC entry 5692 (class 0 OID 279380)
-- Dependencies: 240
-- Data for Name: state; Type: TABLE DATA; Schema: hangfire; Owner: postgres
--



--
-- TOC entry 5694 (class 0 OID 279387)
-- Dependencies: 242
-- Data for Name: __EFMigrationsHistory; Type: TABLE DATA; Schema: public; Owner: postgres
--



--
-- TOC entry 5695 (class 0 OID 279390)
-- Dependencies: 243
-- Data for Name: bank_change_logs; Type: TABLE DATA; Schema: public; Owner: postgres
--



--
-- TOC entry 5697 (class 0 OID 279396)
-- Dependencies: 245
-- Data for Name: bookings; Type: TABLE DATA; Schema: public; Owner: postgres
--

INSERT INTO public.bookings VALUES (64, NULL, '3efeb8ab-3412-441c-ae16-d250a8b58bcd', 'e6752adf-18d5-40e3-9dfd-8facb4614695', NULL, 0.00, 8, 840000.00, '614e20d3629b45d6b270d45fffafb991', NULL, NULL, NULL, NULL, 'deposit_paid', 'DepositEscrowed', NULL, '2026-03-19 05:59:11.283986', '2026-03-19 06:02:30.757907', 420000.00, '2026-03-19 06:02:30.757682', 420000.00, NULL, 'holding', 80000.00, 40000.00, 760000.00, NULL, NULL, NULL, NULL, 'online', '2026-03-19 00:00:00', NULL, NULL, NULL, '2026-03-20 05:59:11.041702', NULL, NULL, NULL, NULL, NULL, NULL, 'VND');
INSERT INTO public.bookings VALUES (65, 'f284548d-85fa-473c-88e0-0b76a08120b1', 'STU-f284548d-85fa-473c-88e0-0b76a08120b1-2', 'e6752adf-18d5-40e3-9dfd-8facb4614695', NULL, 0.00, 4, 8400.00, '168763ca328844b9be284795045c8614', NULL, NULL, NULL, NULL, 'paid', 'Escrowed', NULL, '2026-03-22 15:11:26.012257', '2026-03-22 15:22:40.648943', 4200.00, '2026-03-22 15:20:35.4089', 4200.00, '2026-03-22 15:22:40.648722', 'holding', 800.00, 400.00, 7600.00, NULL, NULL, NULL, NULL, 'online', '2026-03-22 00:00:00', NULL, NULL, NULL, '2026-03-23 15:11:26.001037', NULL, NULL, NULL, NULL, NULL, NULL, 'VND');
INSERT INTO public.bookings VALUES (66, 'f284548d-85fa-473c-88e0-0b76a08120b1', 'STU-f284548d-85fa-473c-88e0-0b76a08120b1-2', 'e6752adf-18d5-40e3-9dfd-8facb4614695', NULL, 0.00, 4, 8400.00, '501DC0668B2F', NULL, NULL, NULL, NULL, 'cancelled', 'Pending', NULL, '2026-03-22 15:29:50.657883', '2026-03-22 15:29:58.121908', NULL, NULL, NULL, NULL, NULL, 800.00, 400.00, 7600.00, 'Tutor declined via chat', 'e6752adf-18d5-40e3-9dfd-8facb4614695', '2026-03-22 15:29:58.121844', NULL, 'online', '2026-03-22 00:00:00', NULL, NULL, NULL, '2026-03-23 15:29:50.654777', NULL, NULL, NULL, NULL, NULL, NULL, 'VND');
INSERT INTO public.bookings VALUES (68, 'f284548d-85fa-473c-88e0-0b76a08120b1', 'STU-f284548d-85fa-473c-88e0-0b76a08120b1-2', 'e6752adf-18d5-40e3-9dfd-8facb4614695', NULL, 0.00, 4, 8400.00, '2d93931441e44fe99dcd256699ca5dd6', NULL, NULL, NULL, NULL, 'paid', 'Escrowed', NULL, '2026-03-23 10:30:52.781309', '2026-03-23 10:39:38.90503', 4200.00, '2026-03-23 10:32:39.901925', 4200.00, '2026-03-23 10:39:38.905029', 'holding', 800.00, 400.00, 7600.00, NULL, NULL, NULL, NULL, 'online', '2026-03-23 00:00:00', NULL, NULL, NULL, '2026-03-24 10:30:52.760194', NULL, NULL, NULL, NULL, NULL, NULL, 'VND');
INSERT INTO public.bookings VALUES (69, 'f284548d-85fa-473c-88e0-0b76a08120b1', 'STU-f284548d-85fa-473c-88e0-0b76a08120b1-2', 'e6752adf-18d5-40e3-9dfd-8facb4614695', NULL, 0.00, 8, 16800.00, '3C995C8AE23F', NULL, NULL, NULL, NULL, 'cancelled', 'Pending', '2026-03-24 10:47:25.313531', '2026-03-23 10:45:53.389038', '2026-03-23 10:50:04.742927', 8400.00, NULL, 8400.00, NULL, NULL, 1600.00, 800.00, 15200.00, 'Ok', 'f284548d-85fa-473c-88e0-0b76a08120b1', '2026-03-23 10:50:04.742925', NULL, 'online', '2026-03-23 00:00:00', NULL, NULL, 'no_refund', '2026-03-24 10:45:53.38629', NULL, NULL, NULL, NULL, NULL, NULL, 'VND');
INSERT INTO public.bookings VALUES (67, 'f284548d-85fa-473c-88e0-0b76a08120b1', 'STU-f284548d-85fa-473c-88e0-0b76a08120b1-2', 'e6752adf-18d5-40e3-9dfd-8facb4614695', NULL, 0.00, 12, 25200.00, 'C7FD44E9F78A', NULL, NULL, NULL, NULL, 'payment_timeout', 'Pending', '2026-03-24 02:46:42.490837', '2026-03-23 02:46:08.819991', '2026-03-24 12:47:03.124743', 12600.00, NULL, 12600.00, NULL, NULL, 2400.00, 1200.00, 22800.00, NULL, NULL, NULL, NULL, 'online', '2026-03-23 00:00:00', NULL, NULL, NULL, '2026-03-24 02:46:08.798596', NULL, NULL, NULL, NULL, NULL, NULL, 'VND');


--
-- TOC entry 5699 (class 0 OID 279405)
-- Dependencies: 247
-- Data for Name: chatchannels; Type: TABLE DATA; Schema: public; Owner: postgres
--

INSERT INTO public.chatchannels VALUES (1, NULL, '2024-02-05 10:00:00', NULL, 'active', NULL, NULL, NULL);
INSERT INTO public.chatchannels VALUES (21, NULL, '2026-03-02 08:56:21.472281', NULL, 'active', NULL, NULL, NULL);
INSERT INTO public.chatchannels VALUES (22, NULL, '2026-03-02 08:58:05.229559', '2026-03-02 08:58:08.800566', 'active', NULL, NULL, NULL);
INSERT INTO public.chatchannels VALUES (20, NULL, '2026-02-18 23:14:56.443305', '2026-03-02 15:35:20.745262', 'active', NULL, NULL, NULL);
INSERT INTO public.chatchannels VALUES (3, NULL, '2026-02-14 13:16:50.225472', '2026-02-15 09:14:22.881957', 'active', NULL, NULL, NULL);
INSERT INTO public.chatchannels VALUES (2, NULL, '2026-02-14 13:16:09.8247', '2026-02-14 13:16:09.949637', 'pending', NULL, NULL, NULL);
INSERT INTO public.chatchannels VALUES (4, NULL, '2026-02-14 13:29:39.287684', '2026-02-15 09:02:28.532804', 'active', NULL, NULL, NULL);
INSERT INTO public.chatchannels VALUES (19, NULL, '2026-02-16 16:19:45.291969', NULL, 'active', NULL, NULL, NULL);
INSERT INTO public.chatchannels VALUES (11, NULL, '2026-02-15 16:31:29.519396', '2026-02-15 16:31:29.531366', 'pending', NULL, NULL, NULL);
INSERT INTO public.chatchannels VALUES (10, NULL, '2026-02-15 16:26:48.465887', NULL, 'pending', NULL, NULL, NULL);
INSERT INTO public.chatchannels VALUES (14, NULL, '2026-02-15 16:45:17.69', '2026-02-15 17:42:41.40947', 'active', NULL, NULL, NULL);
INSERT INTO public.chatchannels VALUES (13, NULL, '2026-02-15 16:43:29.466822', '2026-02-15 16:43:29.480113', 'active', NULL, NULL, NULL);
INSERT INTO public.chatchannels VALUES (12, NULL, '2026-02-15 16:40:31.033151', '2026-02-15 16:40:31.041641', 'active', NULL, NULL, NULL);
INSERT INTO public.chatchannels VALUES (15, NULL, '2026-02-15 17:42:08.969639', '2026-02-15 18:08:13.386227', 'active', NULL, NULL, NULL);
INSERT INTO public.chatchannels VALUES (16, NULL, '2026-02-15 18:08:06.264478', '2026-02-15 18:14:52.576008', 'active', NULL, NULL, NULL);
INSERT INTO public.chatchannels VALUES (17, NULL, '2026-02-15 18:14:26.772752', '2026-02-15 18:24:42.014669', 'active', NULL, NULL, NULL);
INSERT INTO public.chatchannels VALUES (18, NULL, '2026-02-15 18:24:35.052403', '2026-02-16 16:20:44.267748', 'active', NULL, NULL, NULL);
INSERT INTO public.chatchannels VALUES (5, NULL, '2026-02-15 09:27:27.145394', '2026-02-15 09:29:15.892976', 'active', NULL, NULL, NULL);
INSERT INTO public.chatchannels VALUES (6, NULL, '2026-02-15 09:38:37.13139', '2026-02-15 09:52:58.143782', 'pending', NULL, NULL, NULL);
INSERT INTO public.chatchannels VALUES (7, NULL, '2026-02-15 09:39:56.840836', '2026-02-15 15:59:26.061923', 'active', NULL, NULL, NULL);
INSERT INTO public.chatchannels VALUES (8, NULL, '2026-02-15 15:58:06.225105', '2026-02-15 16:12:18.724108', 'active', NULL, NULL, NULL);
INSERT INTO public.chatchannels VALUES (9, NULL, '2026-02-15 16:05:08.701486', '2026-02-15 16:26:48.477671', 'active', NULL, NULL, NULL);
INSERT INTO public.chatchannels VALUES (25, NULL, '2026-03-06 03:49:33.974114', '2026-03-06 03:49:34.227669', 'active', NULL, NULL, '3eb974d3-0789-4f85-b4b1-e08dec437acc');
INSERT INTO public.chatchannels VALUES (24, NULL, '2026-03-04 11:35:59.603138', '2026-03-04 11:38:58.628254', 'active', NULL, NULL, NULL);
INSERT INTO public.chatchannels VALUES (26, NULL, '2026-03-06 03:50:52.658309', '2026-03-06 03:51:30.956938', 'active', NULL, NULL, 'f4ed1495-0d1a-4bb6-a113-c69849fe3de2');
INSERT INTO public.chatchannels VALUES (27, NULL, '2026-03-14 01:12:34.5061', '2026-03-15 07:35:39.979653', 'active', '48a31ca1-cbfd-49d1-845b-d4a923a2c741', NULL, NULL);
INSERT INTO public.chatchannels VALUES (23, NULL, '2026-03-04 04:31:38.439592', NULL, 'active', NULL, NULL, NULL);
INSERT INTO public.chatchannels VALUES (30, NULL, '2026-03-16 16:30:10.209851', '2026-03-18 15:24:20.492374', 'active', 'eb79ec08-d7af-46f3-94cf-a818b2136e10', NULL, NULL);
INSERT INTO public.chatchannels VALUES (29, NULL, '2026-03-16 16:25:46.35417', '2026-03-16 16:26:56.406992', 'active', 'cf457954-3705-41a3-b74f-01c783da0d11', NULL, NULL);
INSERT INTO public.chatchannels VALUES (28, NULL, '2026-03-16 16:15:51.706663', '2026-03-18 16:00:18.079458', 'active', '48a31ca1-cbfd-49d1-845b-d4a923a2c741', NULL, NULL);
INSERT INTO public.chatchannels VALUES (31, NULL, '2026-03-19 05:59:11.442917', '2026-03-19 06:00:49.978186', 'active', NULL, 'e6752adf-18d5-40e3-9dfd-8facb4614695', '3efeb8ab-3412-441c-ae16-d250a8b58bcd');
INSERT INTO public.chatchannels VALUES (32, NULL, '2026-03-22 11:04:41.41678', '2026-03-22 11:07:01.964952', 'active', NULL, 'e6752adf-18d5-40e3-9dfd-8facb4614695', 'a7ed2303-a432-4159-a02a-d597fc136293');
INSERT INTO public.chatchannels VALUES (34, NULL, '2026-03-22 14:35:59.96532', '2026-03-23 10:47:25.325875', 'active', 'f284548d-85fa-473c-88e0-0b76a08120b1', 'e6752adf-18d5-40e3-9dfd-8facb4614695', NULL);
INSERT INTO public.chatchannels VALUES (33, NULL, '2026-03-22 11:06:35.699401', '2026-03-25 14:27:22.721473', 'active', NULL, 'e6752adf-18d5-40e3-9dfd-8facb4614695', '7c696d73-2b77-4b16-961a-224df3d3d786');


--
-- TOC entry 5701 (class 0 OID 279411)
-- Dependencies: 249
-- Data for Name: chatmessages; Type: TABLE DATA; Schema: public; Owner: postgres
--

INSERT INTO public.chatmessages VALUES (82, 25, '3eb974d3-0789-4f85-b4b1-e08dec437acc', '📋 Yêu cầu đặt lịch mới', 'booking_request', '2026-03-06 03:49:34.227627', '{"price": 800000.00, "status": "pending_tutor", "schedule": [{"endTime": "01:00", "dayOfWeek": 4, "startTime": "00:00"}], "bookingId": 48, "finalPrice": 840000.00, "studentName": null, "subjectName": "Toán Học", "sessionCount": 4, "teachingMode": "online"}', false, NULL);
INSERT INTO public.chatmessages VALUES (86, 27, '48a31ca1-cbfd-49d1-845b-d4a923a2c741', '📋 Yêu cầu đặt lịch mới', 'booking_request', '2026-03-14 01:12:34.695669', '{"price": 400000.00, "status": "pending_tutor", "schedule": [{"endTime": "17:00", "dayOfWeek": 5, "startTime": "15:00"}], "bookingId": 51, "finalPrice": 420000.00, "studentName": "Nguyễn Viết Hoàng", "subjectName": "Toán Học", "sessionCount": 4, "teachingMode": "online"}', false, NULL);
INSERT INTO public.chatmessages VALUES (87, 27, '48a31ca1-cbfd-49d1-845b-d4a923a2c741', 'sdfsdf', 'text', '2026-03-15 07:35:39.979651', NULL, false, NULL);
INSERT INTO public.chatmessages VALUES (88, 28, '48a31ca1-cbfd-49d1-845b-d4a923a2c741', '📋 Yêu cầu đặt lịch mới', 'booking_request', '2026-03-16 16:15:51.829794', '{"price": 0.00, "status": "pending_tutor", "schedule": [{"endTime": "09:00", "dayOfWeek": 2, "startTime": "07:00"}], "bookingId": 52, "finalPrice": 0.00, "studentName": "Nguyễn Viết Hoàng", "subjectName": "Ngữ văn", "sessionCount": 4, "teachingMode": "online"}', false, NULL);
INSERT INTO public.chatmessages VALUES (90, 29, 'cf457954-3705-41a3-b74f-01c783da0d11', '📋 Yêu cầu đặt lịch mới', 'booking_request', '2026-03-16 16:25:46.374723', '{"price": 400000.00, "status": "pending_tutor", "schedule": [{"endTime": "05:00", "dayOfWeek": 2, "startTime": "04:00"}], "bookingId": 53, "finalPrice": 420000.00, "studentName": "Dương Thành Phát", "subjectName": "Toán Học", "sessionCount": 4, "teachingMode": "online"}', false, NULL);
INSERT INTO public.chatmessages VALUES (93, 30, 'eb79ec08-d7af-46f3-94cf-a818b2136e10', '📋 Yêu cầu đặt lịch mới', 'booking_request', '2026-03-16 16:30:10.22407', '{"price": 400000.00, "status": "pending_tutor", "schedule": [{"endTime": "07:30", "dayOfWeek": 2, "startTime": "06:30"}], "bookingId": 54, "finalPrice": 420000.00, "studentName": "Lê Thành Công", "subjectName": "Toán Học", "sessionCount": 4, "teachingMode": "online"}', false, NULL);
INSERT INTO public.chatmessages VALUES (94, 30, 'eb79ec08-d7af-46f3-94cf-a818b2136e10', 'hello', 'text', '2026-03-16 16:38:40.444595', NULL, false, NULL);
INSERT INTO public.chatmessages VALUES (96, 28, '48a31ca1-cbfd-49d1-845b-d4a923a2c741', '📋 Yêu cầu đặt lịch mới', 'booking_request', '2026-03-17 06:55:58.989605', '{"price": 1200000.000, "status": "pending_tutor", "schedule": [{"endTime": "08:30", "dayOfWeek": 2, "startTime": "07:00"}], "bookingId": 55, "finalPrice": 1260000.000, "studentName": "Nguyễn Hoàng Ngọc", "subjectName": "Tiếng Anh", "sessionCount": 4, "teachingMode": "online"}', false, NULL);
INSERT INTO public.chatmessages VALUES (98, 28, '48a31ca1-cbfd-49d1-845b-d4a923a2c741', '📋 Yêu cầu đặt lịch mới', 'booking_request', '2026-03-17 07:31:59.507801', '{"price": 1600000.00, "status": "pending_tutor", "schedule": [{"endTime": "06:00", "dayOfWeek": 2, "startTime": "04:00"}], "bookingId": 56, "finalPrice": 1680000.00, "studentName": "Lý Xuân Sơn", "subjectName": "Tiếng Anh", "sessionCount": 4, "teachingMode": "online"}', false, NULL);
INSERT INTO public.chatmessages VALUES (100, 30, 'eb79ec08-d7af-46f3-94cf-a818b2136e10', '📋 Yêu cầu đặt lịch mới', 'booking_request', '2026-03-17 09:32:28.841673', '{"price": 800000.00, "status": "pending_tutor", "schedule": [{"endTime": "07:30", "dayOfWeek": 2, "startTime": "06:30"}], "bookingId": 57, "finalPrice": 840000.00, "studentName": "Lê Thành Công", "subjectName": "Toán Học", "sessionCount": 4, "teachingMode": "online"}', false, NULL);
INSERT INTO public.chatmessages VALUES (102, 30, 'eb79ec08-d7af-46f3-94cf-a818b2136e10', '📋 Yêu cầu đặt lịch mới', 'booking_request', '2026-03-17 10:12:58.004734', '{"price": 800000.00, "status": "pending_tutor", "schedule": [{"endTime": "07:00", "dayOfWeek": 2, "startTime": "06:00"}], "bookingId": 58, "finalPrice": 840000.00, "studentName": "Lê Thành Công", "subjectName": "Toán Học", "sessionCount": 4, "teachingMode": "online"}', false, NULL);
INSERT INTO public.chatmessages VALUES (104, 30, 'eb79ec08-d7af-46f3-94cf-a818b2136e10', '📋 Yêu cầu đặt lịch mới', 'booking_request', '2026-03-17 12:36:41.955735', '{"price": 800000.00, "status": "pending_tutor", "schedule": [{"endTime": "07:30", "dayOfWeek": 2, "startTime": "06:30"}], "bookingId": 59, "finalPrice": 840000.00, "studentName": "Lê Thành Công", "subjectName": "Toán Học", "sessionCount": 4, "teachingMode": "online"}', false, NULL);
INSERT INTO public.chatmessages VALUES (106, 30, 'eb79ec08-d7af-46f3-94cf-a818b2136e10', '📋 Yêu cầu đặt lịch mới', 'booking_request', '2026-03-17 12:51:09.567104', '{"price": 800000.00, "status": "pending_tutor", "schedule": [{"endTime": "07:00", "dayOfWeek": 2, "startTime": "06:00"}], "bookingId": 60, "finalPrice": 840000.00, "studentName": "Lê Thành Công", "subjectName": "Toán Học", "sessionCount": 4, "teachingMode": "online"}', false, NULL);
INSERT INTO public.chatmessages VALUES (108, 28, '48a31ca1-cbfd-49d1-845b-d4a923a2c741', '📋 Yêu cầu đặt lịch mới', 'booking_request', '2026-03-17 13:22:26.324125', '{"price": 8000.00, "status": "pending_tutor", "schedule": [{"endTime": "06:00", "dayOfWeek": 2, "startTime": "04:00"}], "bookingId": 61, "finalPrice": 8400.00, "studentName": "Lý Xuân Nguyên", "subjectName": "Ngữ văn", "sessionCount": 4, "teachingMode": "online"}', false, NULL);
INSERT INTO public.chatmessages VALUES (110, 30, 'eb79ec08-d7af-46f3-94cf-a818b2136e10', 'cmm', 'text', '2026-03-18 14:48:15.911803', NULL, false, NULL);
INSERT INTO public.chatmessages VALUES (111, 28, '48a31ca1-cbfd-49d1-845b-d4a923a2c741', 'Hello', 'text', '2026-03-18 14:49:37.010258', NULL, false, NULL);
INSERT INTO public.chatmessages VALUES (112, 28, '48a31ca1-cbfd-49d1-845b-d4a923a2c741', 'Alo', 'text', '2026-03-18 14:49:49.931368', NULL, false, NULL);
INSERT INTO public.chatmessages VALUES (113, 28, '48a31ca1-cbfd-49d1-845b-d4a923a2c741', '📋 Yêu cầu đặt lịch mới', 'booking_request', '2026-03-18 14:51:44.660813', '{"price": 8000.00, "status": "pending_tutor", "schedule": [{"endTime": "06:00", "dayOfWeek": 2, "startTime": "04:00"}], "bookingId": 62, "finalPrice": 8400.00, "studentName": "Nguyễn Hoàng Ngọc", "subjectName": "Ngữ văn", "sessionCount": 4, "teachingMode": "online"}', false, NULL);
INSERT INTO public.chatmessages VALUES (115, 28, '48a31ca1-cbfd-49d1-845b-d4a923a2c741', '📋 Yêu cầu đặt lịch mới', 'booking_request', '2026-03-18 14:57:57.027875', '{"price": 4000.00, "status": "pending_tutor", "schedule": [{"endTime": "09:00", "dayOfWeek": 2, "startTime": "08:00"}], "bookingId": 63, "finalPrice": 4200.00, "studentName": "Lý Xuân Nguyên", "subjectName": "Ngữ văn", "sessionCount": 4, "teachingMode": "online"}', false, NULL);
INSERT INTO public.chatmessages VALUES (119, 30, 'eb79ec08-d7af-46f3-94cf-a818b2136e10', 'aa', 'text', '2026-03-18 15:23:25.969902', NULL, false, NULL);
INSERT INTO public.chatmessages VALUES (120, 30, 'eb79ec08-d7af-46f3-94cf-a818b2136e10', 'aa', 'text', '2026-03-18 15:23:48.21867', NULL, false, NULL);
INSERT INTO public.chatmessages VALUES (121, 30, 'eb79ec08-d7af-46f3-94cf-a818b2136e10', 'sadsad', 'text', '2026-03-18 15:24:01.084843', NULL, false, NULL);
INSERT INTO public.chatmessages VALUES (122, 30, 'eb79ec08-d7af-46f3-94cf-a818b2136e10', 'aa', 'text', '2026-03-18 15:24:09.652534', NULL, false, NULL);
INSERT INTO public.chatmessages VALUES (123, 30, 'eb79ec08-d7af-46f3-94cf-a818b2136e10', 'sdadsa', 'text', '2026-03-18 15:24:20.492373', NULL, false, NULL);
INSERT INTO public.chatmessages VALUES (124, 28, '48a31ca1-cbfd-49d1-845b-d4a923a2c741', 'Alo', 'text', '2026-03-18 15:59:29.884963', NULL, false, NULL);
INSERT INTO public.chatmessages VALUES (125, 28, '48a31ca1-cbfd-49d1-845b-d4a923a2c741', 'Hello', 'text', '2026-03-18 15:59:58.832338', NULL, false, NULL);
INSERT INTO public.chatmessages VALUES (128, 31, 'e6752adf-18d5-40e3-9dfd-8facb4614695', '✅ Gia sư đã chấp nhận yêu cầu đặt lịch', 'booking_accepted', '2026-03-19 06:00:49.978185', '{"status": "accepted", "bookingId": 64}', false, NULL);
INSERT INTO public.chatmessages VALUES (130, 32, 'a7ed2303-a432-4159-a02a-d597fc136293', 'Hello', 'text', '2026-03-22 11:07:01.964951', NULL, true, '2026-03-25 12:53:24.586979');
INSERT INTO public.chatmessages VALUES (131, 34, 'f284548d-85fa-473c-88e0-0b76a08120b1', 'Alo', 'text', '2026-03-22 14:45:46.479292', NULL, true, '2026-03-25 12:54:17.796125');
INSERT INTO public.chatmessages VALUES (132, 34, 'f284548d-85fa-473c-88e0-0b76a08120b1', 'Alo', 'text', '2026-03-22 14:46:16.576975', NULL, true, '2026-03-25 12:54:17.796125');
INSERT INTO public.chatmessages VALUES (133, 34, 'f284548d-85fa-473c-88e0-0b76a08120b1', '📋 Yêu cầu đặt lịch mới', 'booking_request', '2026-03-22 15:11:26.043342', '{"price": 8000.00, "status": "pending_tutor", "schedule": [{"endTime": "10:30", "dayOfWeek": 1, "startTime": "08:30"}], "bookingId": 65, "finalPrice": 8400.00, "studentName": "Lê Thị Huế Trân", "subjectName": "Toán Học", "sessionCount": 4, "teachingMode": "online"}', true, '2026-03-25 12:54:17.796125');
INSERT INTO public.chatmessages VALUES (135, 34, 'f284548d-85fa-473c-88e0-0b76a08120b1', '📋 Yêu cầu đặt lịch mới', 'booking_request', '2026-03-22 15:29:50.686938', '{"price": 8000.00, "status": "pending_tutor", "schedule": [{"endTime": "09:00", "dayOfWeek": 1, "startTime": "07:00"}], "bookingId": 66, "finalPrice": 8400.00, "studentName": "Lê Thị Huế Trân", "subjectName": "Tiếng Anh", "sessionCount": 4, "teachingMode": "online"}', true, '2026-03-25 12:54:17.796125');
INSERT INTO public.chatmessages VALUES (129, 33, '7c696d73-2b77-4b16-961a-224df3d3d786', 'Hello', 'text', '2026-03-22 11:06:44.293102', NULL, true, '2026-03-25 12:54:38.878283');
INSERT INTO public.chatmessages VALUES (127, 31, '3efeb8ab-3412-441c-ae16-d250a8b58bcd', '📋 Yêu cầu đặt lịch mới', 'booking_request', '2026-03-19 05:59:11.584873', '{"price": 800000.00, "status": "pending_tutor", "schedule": [{"endTime": "10:30", "dayOfWeek": 1, "startTime": "08:30"}, {"endTime": "10:30", "dayOfWeek": 3, "startTime": "08:30"}], "bookingId": 64, "finalPrice": 840000.00, "studentName": null, "subjectName": "Tiếng Anh", "sessionCount": 8, "teachingMode": "online"}', true, '2026-03-25 13:04:05.238208');
INSERT INTO public.chatmessages VALUES (134, 34, 'e6752adf-18d5-40e3-9dfd-8facb4614695', '✅ Gia sư đã chấp nhận yêu cầu đặt lịch', 'booking_accepted', '2026-03-22 15:18:47.736889', '{"status": "accepted", "bookingId": 65}', true, '2026-03-25 14:33:57.440796');
INSERT INTO public.chatmessages VALUES (137, 34, 'e6752adf-18d5-40e3-9dfd-8facb4614695', '✅ Gia sư đã chấp nhận yêu cầu đặt lịch', 'booking_accepted', '2026-03-23 02:46:42.500824', '{"status": "accepted", "bookingId": 67}', true, '2026-03-25 14:33:57.440796');
INSERT INTO public.chatmessages VALUES (139, 34, 'e6752adf-18d5-40e3-9dfd-8facb4614695', '✅ Gia sư đã chấp nhận yêu cầu đặt lịch', 'booking_accepted', '2026-03-23 10:31:43.008331', '{"status": "accepted", "bookingId": 68}', true, '2026-03-25 14:33:57.440796');
INSERT INTO public.chatmessages VALUES (136, 34, 'f284548d-85fa-473c-88e0-0b76a08120b1', '📋 Yêu cầu đặt lịch mới', 'booking_request', '2026-03-23 02:46:08.866733', '{"price": 24000.00, "status": "pending_tutor", "schedule": [{"endTime": "11:00", "dayOfWeek": 1, "startTime": "09:00"}, {"endTime": "11:00", "dayOfWeek": 3, "startTime": "09:00"}, {"endTime": "11:00", "dayOfWeek": 4, "startTime": "09:00"}], "bookingId": 67, "finalPrice": 25200.00, "studentName": "Lê Thị Huế Trân", "subjectName": "Toán Học", "sessionCount": 12, "teachingMode": "online"}', true, '2026-03-25 12:54:17.796125');
INSERT INTO public.chatmessages VALUES (138, 34, 'f284548d-85fa-473c-88e0-0b76a08120b1', '📋 Yêu cầu đặt lịch mới', 'booking_request', '2026-03-23 10:30:52.834121', '{"price": 8000.00, "status": "pending_tutor", "schedule": [{"endTime": "10:00", "dayOfWeek": 1, "startTime": "08:00"}], "bookingId": 68, "finalPrice": 8400.00, "studentName": "Lê Thị Huế Trân", "subjectName": "Toán Học", "sessionCount": 4, "teachingMode": "online"}', true, '2026-03-25 12:54:17.796125');
INSERT INTO public.chatmessages VALUES (140, 34, 'f284548d-85fa-473c-88e0-0b76a08120b1', '📋 Yêu cầu đặt lịch mới', 'booking_request', '2026-03-23 10:45:53.415834', '{"price": 16000.00, "status": "pending_tutor", "schedule": [{"endTime": "10:00", "dayOfWeek": 1, "startTime": "08:00"}, {"endTime": "13:00", "dayOfWeek": 1, "startTime": "11:00"}], "bookingId": 69, "finalPrice": 16800.00, "studentName": "Lê Thị Huế Trân", "subjectName": "Tiếng Anh", "sessionCount": 8, "teachingMode": "online"}', true, '2026-03-25 12:54:17.796125');
INSERT INTO public.chatmessages VALUES (142, 33, '7c696d73-2b77-4b16-961a-224df3d3d786', 'https://outlxbcgkcuximxydlvq.supabase.co/storage/v1/object/sign/chat-images/7c696d73-2b77-4b16-961a-224df3d3d786/5f3328f7-831d-47ac-8729-2ec19f672bda.jpg?token=eyJraWQiOiJzdG9yYWdlLXVybC1zaWduaW5nLWtleV82MTA4N2ZkMC0zYjUwLTQ2NTQtODYwOS1lMGFmYzA3ZTQwNTMiLCJhbGciOiJIUzI1NiJ9.eyJ1cmwiOiJjaGF0LWltYWdlcy83YzY5NmQ3My0yYjc3LTRiMTYtOTYxYS0yMjRkZjNkM2Q3ODYvNWYzMzI4ZjctODMxZC00N2FjLTg3MjktMmVjMTlmNjcyYmRhLmpwZyIsImlhdCI6MTc3NDQ0MzI0NiwiZXhwIjoxODA1OTc5MjQ2fQ.4KRqeytGI1nZAZjtRU6afoP1zpmPNFoc0HNTcpzFav8', 'image', '2026-03-25 12:54:06.495753', NULL, true, '2026-03-25 12:54:38.878283');
INSERT INTO public.chatmessages VALUES (143, 33, '7c696d73-2b77-4b16-961a-224df3d3d786', 'hello', 'text', '2026-03-25 12:54:45.119873', NULL, true, '2026-03-25 13:04:07.330801');
INSERT INTO public.chatmessages VALUES (144, 33, '7c696d73-2b77-4b16-961a-224df3d3d786', 'https://outlxbcgkcuximxydlvq.supabase.co/storage/v1/object/sign/chat-images/7c696d73-2b77-4b16-961a-224df3d3d786/8ad8c8a7-cfa6-4a40-a9de-1a10fef3b077.jpg?token=eyJraWQiOiJzdG9yYWdlLXVybC1zaWduaW5nLWtleV82MTA4N2ZkMC0zYjUwLTQ2NTQtODYwOS1lMGFmYzA3ZTQwNTMiLCJhbGciOiJIUzI1NiJ9.eyJ1cmwiOiJjaGF0LWltYWdlcy83YzY5NmQ3My0yYjc3LTRiMTYtOTYxYS0yMjRkZjNkM2Q3ODYvOGFkOGM4YTctY2ZhNi00YTQwLWE5ZGUtMWExMGZlZjNiMDc3LmpwZyIsImlhdCI6MTc3NDQ0MzMwMSwiZXhwIjoxODA1OTc5MzAxfQ.5rwCglEAMwmOLTZHdJ3hO5gjOcj1IbETCSyipxPnI24', 'image', '2026-03-25 12:55:02.087657', NULL, true, '2026-03-25 13:04:07.330801');
INSERT INTO public.chatmessages VALUES (145, 33, '7c696d73-2b77-4b16-961a-224df3d3d786', 'https://outlxbcgkcuximxydlvq.supabase.co/storage/v1/object/sign/chat-images/7c696d73-2b77-4b16-961a-224df3d3d786/dd082358-4c67-48c6-86c5-d083cc7d9a9d.jpg?token=eyJraWQiOiJzdG9yYWdlLXVybC1zaWduaW5nLWtleV82MTA4N2ZkMC0zYjUwLTQ2NTQtODYwOS1lMGFmYzA3ZTQwNTMiLCJhbGciOiJIUzI1NiJ9.eyJ1cmwiOiJjaGF0LWltYWdlcy83YzY5NmQ3My0yYjc3LTRiMTYtOTYxYS0yMjRkZjNkM2Q3ODYvZGQwODIzNTgtNGM2Ny00OGM2LTg2YzUtZDA4M2NjN2Q5YTlkLmpwZyIsImlhdCI6MTc3NDQ0MzMxOCwiZXhwIjoxODA1OTc5MzE4fQ.gl9jwJHL5TPAekX2GdaddfOsfAKvtVYS9UHRw77Fxh4', 'image', '2026-03-25 12:55:18.557305', NULL, true, '2026-03-25 13:04:07.330801');
INSERT INTO public.chatmessages VALUES (146, 33, '7c696d73-2b77-4b16-961a-224df3d3d786', 'https://outlxbcgkcuximxydlvq.supabase.co/storage/v1/object/sign/chat-images/7c696d73-2b77-4b16-961a-224df3d3d786/7c5cd8ec-3747-4c88-b1e3-fc237fa2850c.jpg?token=eyJraWQiOiJzdG9yYWdlLXVybC1zaWduaW5nLWtleV82MTA4N2ZkMC0zYjUwLTQ2NTQtODYwOS1lMGFmYzA3ZTQwNTMiLCJhbGciOiJIUzI1NiJ9.eyJ1cmwiOiJjaGF0LWltYWdlcy83YzY5NmQ3My0yYjc3LTRiMTYtOTYxYS0yMjRkZjNkM2Q3ODYvN2M1Y2Q4ZWMtMzc0Ny00Yzg4LWIxZTMtZmMyMzdmYTI4NTBjLmpwZyIsImlhdCI6MTc3NDQ0MzMzOCwiZXhwIjoxODA1OTc5MzM4fQ.pTRD0as_8tXTUeTiDrHwk6oqODtwqUX0FY9p4Xw1IQM', 'image', '2026-03-25 12:55:38.791885', NULL, true, '2026-03-25 13:04:07.330801');
INSERT INTO public.chatmessages VALUES (147, 33, '7c696d73-2b77-4b16-961a-224df3d3d786', 'https://outlxbcgkcuximxydlvq.supabase.co/storage/v1/object/sign/chat-images/7c696d73-2b77-4b16-961a-224df3d3d786/ecc20489-bf63-4810-8531-682c3e3c1f65.jpg?token=eyJraWQiOiJzdG9yYWdlLXVybC1zaWduaW5nLWtleV82MTA4N2ZkMC0zYjUwLTQ2NTQtODYwOS1lMGFmYzA3ZTQwNTMiLCJhbGciOiJIUzI1NiJ9.eyJ1cmwiOiJjaGF0LWltYWdlcy83YzY5NmQ3My0yYjc3LTRiMTYtOTYxYS0yMjRkZjNkM2Q3ODYvZWNjMjA0ODktYmY2My00ODEwLTg1MzEtNjgyYzNlM2MxZjY1LmpwZyIsImlhdCI6MTc3NDQ0ODExOCwiZXhwIjoxODA1OTg0MTE4fQ.qdY039cE2lTgjFY3BklaccpWQXJwZS0HmNI1eGuCmrE', 'image', '2026-03-25 14:15:18.24375', NULL, false, NULL);
INSERT INTO public.chatmessages VALUES (148, 33, '7c696d73-2b77-4b16-961a-224df3d3d786', 'https://outlxbcgkcuximxydlvq.supabase.co/storage/v1/object/sign/chat-images/7c696d73-2b77-4b16-961a-224df3d3d786/5ddb3928-9f54-489c-844f-87f0460681c4.jpg?token=eyJraWQiOiJzdG9yYWdlLXVybC1zaWduaW5nLWtleV82MTA4N2ZkMC0zYjUwLTQ2NTQtODYwOS1lMGFmYzA3ZTQwNTMiLCJhbGciOiJIUzI1NiJ9.eyJ1cmwiOiJjaGF0LWltYWdlcy83YzY5NmQ3My0yYjc3LTRiMTYtOTYxYS0yMjRkZjNkM2Q3ODYvNWRkYjM5MjgtOWY1NC00ODljLTg0NGYtODdmMDQ2MDY4MWM0LmpwZyIsImlhdCI6MTc3NDQ0ODg0MiwiZXhwIjoxODA1OTg0ODQyfQ.Ejh9x2CQvRKwx6vHUdRgfyqnP3oAbpn2iv5rUkszjbA', 'image', '2026-03-25 14:27:22.721471', NULL, false, NULL);
INSERT INTO public.chatmessages VALUES (141, 34, 'e6752adf-18d5-40e3-9dfd-8facb4614695', '✅ Gia sư đã chấp nhận yêu cầu đặt lịch', 'booking_accepted', '2026-03-23 10:47:25.325874', '{"status": "accepted", "bookingId": 69}', true, '2026-03-25 14:33:57.440796');


--
-- TOC entry 5703 (class 0 OID 279420)
-- Dependencies: 251
-- Data for Name: chatparticipants; Type: TABLE DATA; Schema: public; Owner: postgres
--

INSERT INTO public.chatparticipants VALUES (25, '3eb974d3-0789-4f85-b4b1-e08dec437acc');
INSERT INTO public.chatparticipants VALUES (27, '48a31ca1-cbfd-49d1-845b-d4a923a2c741');
INSERT INTO public.chatparticipants VALUES (28, '48a31ca1-cbfd-49d1-845b-d4a923a2c741');
INSERT INTO public.chatparticipants VALUES (29, 'cf457954-3705-41a3-b74f-01c783da0d11');
INSERT INTO public.chatparticipants VALUES (30, 'eb79ec08-d7af-46f3-94cf-a818b2136e10');
INSERT INTO public.chatparticipants VALUES (31, '3efeb8ab-3412-441c-ae16-d250a8b58bcd');
INSERT INTO public.chatparticipants VALUES (31, 'e6752adf-18d5-40e3-9dfd-8facb4614695');
INSERT INTO public.chatparticipants VALUES (32, 'a7ed2303-a432-4159-a02a-d597fc136293');
INSERT INTO public.chatparticipants VALUES (32, 'e6752adf-18d5-40e3-9dfd-8facb4614695');
INSERT INTO public.chatparticipants VALUES (33, '7c696d73-2b77-4b16-961a-224df3d3d786');
INSERT INTO public.chatparticipants VALUES (33, 'e6752adf-18d5-40e3-9dfd-8facb4614695');
INSERT INTO public.chatparticipants VALUES (34, 'e6752adf-18d5-40e3-9dfd-8facb4614695');
INSERT INTO public.chatparticipants VALUES (34, 'f284548d-85fa-473c-88e0-0b76a08120b1');


--
-- TOC entry 5704 (class 0 OID 279423)
-- Dependencies: 252
-- Data for Name: classes; Type: TABLE DATA; Schema: public; Owner: postgres
--



--
-- TOC entry 5706 (class 0 OID 279429)
-- Dependencies: 254
-- Data for Name: disputes; Type: TABLE DATA; Schema: public; Owner: postgres
--



--
-- TOC entry 5708 (class 0 OID 279437)
-- Dependencies: 256
-- Data for Name: feedbacks; Type: TABLE DATA; Schema: public; Owner: postgres
--



--
-- TOC entry 5710 (class 0 OID 279447)
-- Dependencies: 258
-- Data for Name: fraud_logs; Type: TABLE DATA; Schema: public; Owner: postgres
--



--
-- TOC entry 5758 (class 0 OID 295568)
-- Dependencies: 306
-- Data for Name: gradelevels; Type: TABLE DATA; Schema: public; Owner: postgres
--

INSERT INTO public.gradelevels VALUES (1, 'Lớp 1', 1, '2026-06-05 14:39:03.028605');
INSERT INTO public.gradelevels VALUES (2, 'Lớp 2', 2, '2026-06-05 14:39:03.028605');
INSERT INTO public.gradelevels VALUES (3, 'Lớp 3', 3, '2026-06-05 14:39:03.028605');
INSERT INTO public.gradelevels VALUES (4, 'Lớp 4', 4, '2026-06-05 14:39:03.028605');
INSERT INTO public.gradelevels VALUES (5, 'Lớp 5', 5, '2026-06-05 14:39:03.028605');
INSERT INTO public.gradelevels VALUES (6, 'Lớp 6', 6, '2026-06-05 14:39:03.028605');
INSERT INTO public.gradelevels VALUES (7, 'Lớp 7', 7, '2026-06-05 14:39:03.028605');
INSERT INTO public.gradelevels VALUES (8, 'Lớp 8', 8, '2026-06-05 14:39:03.028605');
INSERT INTO public.gradelevels VALUES (9, 'Lớp 9', 9, '2026-06-05 14:39:03.028605');
INSERT INTO public.gradelevels VALUES (10, 'Lớp 10', 10, '2026-06-05 14:39:03.028605');
INSERT INTO public.gradelevels VALUES (11, 'Lớp 11', 11, '2026-06-05 14:39:03.028605');
INSERT INTO public.gradelevels VALUES (12, 'Lớp 12', 12, '2026-06-05 14:39:03.028605');


--
-- TOC entry 5712 (class 0 OID 279456)
-- Dependencies: 260
-- Data for Name: handoversummaries; Type: TABLE DATA; Schema: public; Owner: postgres
--



--
-- TOC entry 5714 (class 0 OID 279463)
-- Dependencies: 262
-- Data for Name: learningmaterials; Type: TABLE DATA; Schema: public; Owner: postgres
--



--
-- TOC entry 5716 (class 0 OID 279471)
-- Dependencies: 264
-- Data for Name: lessonreports; Type: TABLE DATA; Schema: public; Owner: postgres
--



--
-- TOC entry 5718 (class 0 OID 279478)
-- Dependencies: 266
-- Data for Name: lessons; Type: TABLE DATA; Schema: public; Owner: postgres
--

INSERT INTO public.lessons VALUES (79, 65, 'e6752adf-18d5-40e3-9dfd-8facb4614695', 'STU-f284548d-85fa-473c-88e0-0b76a08120b1-2', '2026-04-20 01:30:00', '2026-04-20 03:30:00', NULL, NULL, NULL, NULL, NULL, NULL, 'scheduled', NULL, NULL, NULL, NULL, 2000.00, false, NULL, NULL, NULL, NULL, NULL, false, NULL, false, NULL, NULL, '2026-03-22 15:20:35.458331', NULL, NULL);
INSERT INTO public.lessons VALUES (80, 65, 'e6752adf-18d5-40e3-9dfd-8facb4614695', 'STU-f284548d-85fa-473c-88e0-0b76a08120b1-2', '2026-04-27 01:30:00', '2026-04-27 03:30:00', NULL, NULL, NULL, NULL, NULL, NULL, 'scheduled', NULL, NULL, NULL, NULL, 2000.00, false, NULL, NULL, NULL, NULL, NULL, false, NULL, false, NULL, NULL, '2026-03-22 15:20:35.459675', NULL, NULL);
INSERT INTO public.lessons VALUES (81, 65, 'e6752adf-18d5-40e3-9dfd-8facb4614695', 'STU-f284548d-85fa-473c-88e0-0b76a08120b1-2', '2026-05-04 01:30:00', '2026-05-04 03:30:00', NULL, NULL, NULL, NULL, NULL, NULL, 'scheduled', NULL, NULL, NULL, NULL, 2000.00, false, NULL, NULL, NULL, NULL, NULL, false, NULL, false, NULL, NULL, '2026-03-22 15:20:35.459921', NULL, NULL);
INSERT INTO public.lessons VALUES (82, 65, 'e6752adf-18d5-40e3-9dfd-8facb4614695', 'STU-f284548d-85fa-473c-88e0-0b76a08120b1-2', '2026-05-11 01:30:00', '2026-05-11 03:30:00', NULL, NULL, NULL, NULL, NULL, NULL, 'scheduled', NULL, NULL, NULL, NULL, 2000.00, false, NULL, NULL, NULL, NULL, NULL, false, NULL, false, NULL, NULL, '2026-03-22 15:20:35.459984', NULL, NULL);
INSERT INTO public.lessons VALUES (71, 64, 'e6752adf-18d5-40e3-9dfd-8facb4614695', '3efeb8ab-3412-441c-ae16-d250a8b58bcd', '2026-03-23 01:30:00', '2026-03-23 03:30:00', NULL, NULL, NULL, NULL, NULL, NULL, 'scheduled', NULL, NULL, NULL, NULL, 100000.00, false, NULL, NULL, NULL, NULL, NULL, true, '2026-03-23 01:05:08.835327', false, NULL, NULL, '2026-03-19 06:02:30.931448', NULL, NULL);
INSERT INTO public.lessons VALUES (83, 68, 'e6752adf-18d5-40e3-9dfd-8facb4614695', 'STU-f284548d-85fa-473c-88e0-0b76a08120b1-2', '2026-05-18 01:00:00', '2026-05-18 03:00:00', NULL, NULL, NULL, NULL, NULL, NULL, 'scheduled', NULL, NULL, NULL, NULL, 2000.00, false, NULL, NULL, NULL, NULL, NULL, false, NULL, false, NULL, NULL, '2026-03-23 10:32:39.9857', NULL, NULL);
INSERT INTO public.lessons VALUES (84, 68, 'e6752adf-18d5-40e3-9dfd-8facb4614695', 'STU-f284548d-85fa-473c-88e0-0b76a08120b1-2', '2026-05-25 01:00:00', '2026-05-25 03:00:00', NULL, NULL, NULL, NULL, NULL, NULL, 'scheduled', NULL, NULL, NULL, NULL, 2000.00, false, NULL, NULL, NULL, NULL, NULL, false, NULL, false, NULL, NULL, '2026-03-23 10:32:39.98723', NULL, NULL);
INSERT INTO public.lessons VALUES (85, 68, 'e6752adf-18d5-40e3-9dfd-8facb4614695', 'STU-f284548d-85fa-473c-88e0-0b76a08120b1-2', '2026-06-01 01:00:00', '2026-06-01 03:00:00', NULL, NULL, NULL, NULL, NULL, NULL, 'scheduled', NULL, NULL, NULL, NULL, 2000.00, false, NULL, NULL, NULL, NULL, NULL, false, NULL, false, NULL, NULL, '2026-03-23 10:32:39.987383', NULL, NULL);
INSERT INTO public.lessons VALUES (86, 68, 'e6752adf-18d5-40e3-9dfd-8facb4614695', 'STU-f284548d-85fa-473c-88e0-0b76a08120b1-2', '2026-06-08 01:00:00', '2026-06-08 03:00:00', NULL, NULL, NULL, NULL, NULL, NULL, 'scheduled', NULL, NULL, NULL, NULL, 2000.00, false, NULL, NULL, NULL, NULL, NULL, false, NULL, false, NULL, NULL, '2026-03-23 10:32:39.987474', NULL, NULL);
INSERT INTO public.lessons VALUES (72, 64, 'e6752adf-18d5-40e3-9dfd-8facb4614695', '3efeb8ab-3412-441c-ae16-d250a8b58bcd', '2026-03-25 01:30:00', '2026-03-25 03:30:00', NULL, NULL, NULL, NULL, NULL, NULL, 'scheduled', NULL, NULL, NULL, NULL, 100000.00, false, NULL, NULL, NULL, NULL, NULL, true, '2026-03-25 01:02:35.883962', false, NULL, NULL, '2026-03-19 06:02:31.023469', NULL, NULL);
INSERT INTO public.lessons VALUES (73, 64, 'e6752adf-18d5-40e3-9dfd-8facb4614695', '3efeb8ab-3412-441c-ae16-d250a8b58bcd', '2026-03-30 01:30:00', '2026-03-30 03:30:00', NULL, NULL, NULL, NULL, NULL, NULL, 'scheduled', NULL, NULL, NULL, NULL, 100000.00, false, NULL, NULL, NULL, NULL, NULL, false, NULL, false, NULL, NULL, '2026-03-19 06:02:31.023783', NULL, NULL);
INSERT INTO public.lessons VALUES (74, 64, 'e6752adf-18d5-40e3-9dfd-8facb4614695', '3efeb8ab-3412-441c-ae16-d250a8b58bcd', '2026-04-01 01:30:00', '2026-04-01 03:30:00', NULL, NULL, NULL, NULL, NULL, NULL, 'scheduled', NULL, NULL, NULL, NULL, 100000.00, false, NULL, NULL, NULL, NULL, NULL, false, NULL, false, NULL, NULL, '2026-03-19 06:02:31.023868', NULL, NULL);
INSERT INTO public.lessons VALUES (75, 64, 'e6752adf-18d5-40e3-9dfd-8facb4614695', '3efeb8ab-3412-441c-ae16-d250a8b58bcd', '2026-04-06 01:30:00', '2026-04-06 03:30:00', NULL, NULL, NULL, NULL, NULL, NULL, 'scheduled', NULL, NULL, NULL, NULL, 100000.00, false, NULL, NULL, NULL, NULL, NULL, false, NULL, false, NULL, NULL, '2026-03-19 06:02:31.023919', NULL, NULL);
INSERT INTO public.lessons VALUES (76, 64, 'e6752adf-18d5-40e3-9dfd-8facb4614695', '3efeb8ab-3412-441c-ae16-d250a8b58bcd', '2026-04-08 01:30:00', '2026-04-08 03:30:00', NULL, NULL, NULL, NULL, NULL, NULL, 'scheduled', NULL, NULL, NULL, NULL, 100000.00, false, NULL, NULL, NULL, NULL, NULL, false, NULL, false, NULL, NULL, '2026-03-19 06:02:31.023981', NULL, NULL);
INSERT INTO public.lessons VALUES (77, 64, 'e6752adf-18d5-40e3-9dfd-8facb4614695', '3efeb8ab-3412-441c-ae16-d250a8b58bcd', '2026-04-13 01:30:00', '2026-04-13 03:30:00', NULL, NULL, NULL, NULL, NULL, NULL, 'scheduled', NULL, NULL, NULL, NULL, 100000.00, false, NULL, NULL, NULL, NULL, NULL, false, NULL, false, NULL, NULL, '2026-03-19 06:02:31.024027', NULL, NULL);
INSERT INTO public.lessons VALUES (78, 64, 'e6752adf-18d5-40e3-9dfd-8facb4614695', '3efeb8ab-3412-441c-ae16-d250a8b58bcd', '2026-04-15 01:30:00', '2026-04-15 03:30:00', NULL, NULL, NULL, NULL, NULL, NULL, 'scheduled', NULL, NULL, NULL, NULL, 100000.00, false, NULL, NULL, NULL, NULL, NULL, false, NULL, false, NULL, NULL, '2026-03-19 06:02:31.024125', NULL, NULL);


--
-- TOC entry 5720 (class 0 OID 279489)
-- Dependencies: 268
-- Data for Name: login_history; Type: TABLE DATA; Schema: public; Owner: postgres
--



--
-- TOC entry 5722 (class 0 OID 279496)
-- Dependencies: 270
-- Data for Name: notifications; Type: TABLE DATA; Schema: public; Owner: postgres
--

INSERT INTO public.notifications VALUES (122, 'e6752adf-18d5-40e3-9dfd-8facb4614695', 'New booking request', 'You have a new booking request. Note: Parent requested ''online'' mode, which differs from your preferred ''both'' mode. Payment code: 5878A546BC25', 'booking_new', '64', false, '2026-03-19 05:59:11.303592', 'app', NULL, NULL);
INSERT INTO public.notifications VALUES (124, 'e6752adf-18d5-40e3-9dfd-8facb4614695', 'Tin nhắn mới', 'Bạn có tin nhắn mới trong cuộc trò chuyện', NULL, NULL, false, '2026-03-19 05:59:11.63975', 'app', NULL, NULL);
INSERT INTO public.notifications VALUES (125, '3efeb8ab-3412-441c-ae16-d250a8b58bcd', 'Tin nhắn mới', 'Bạn có tin nhắn mới trong cuộc trò chuyện', NULL, NULL, false, '2026-03-19 06:00:49.992956', 'app', NULL, NULL);
INSERT INTO public.notifications VALUES (126, NULL, 'Cọc thành công', 'Đã thanh toán cọc 50% (420,000đ) cho booking #64. Gia sư sẽ bắt đầu dạy buổi đầu tiên.', 'payment_success', '64', false, '2026-03-19 06:02:30.809903', 'app', NULL, NULL);
INSERT INTO public.notifications VALUES (127, 'e6752adf-18d5-40e3-9dfd-8facb4614695', 'Booking đã được cọc', 'Phụ huynh đã cọc 50% cho booking #64. Bạn có thể bắt đầu dạy.', 'payment_success', '64', false, '2026-03-19 06:02:30.810127', 'app', NULL, NULL);
INSERT INTO public.notifications VALUES (130, 'e6752adf-18d5-40e3-9dfd-8facb4614695', 'Tin nhắn mới', 'Bạn có tin nhắn mới trong cuộc trò chuyện', NULL, NULL, false, '2026-03-22 14:45:46.496206', 'app', NULL, NULL);
INSERT INTO public.notifications VALUES (131, 'e6752adf-18d5-40e3-9dfd-8facb4614695', 'Tin nhắn mới', 'Bạn có tin nhắn mới trong cuộc trò chuyện', NULL, NULL, false, '2026-03-22 14:46:16.600519', 'app', NULL, NULL);
INSERT INTO public.notifications VALUES (135, 'f284548d-85fa-473c-88e0-0b76a08120b1', 'Tin nhắn mới', 'Bạn có tin nhắn mới trong cuộc trò chuyện', NULL, NULL, false, '2026-03-22 15:18:47.761319', 'app', NULL, NULL);
INSERT INTO public.notifications VALUES (136, 'f284548d-85fa-473c-88e0-0b76a08120b1', 'Booking accepted', 'Gia sư đã chấp nhận yêu cầu đặt lịch #65', NULL, NULL, true, '2026-03-22 15:18:47.771678', 'app', NULL, NULL);
INSERT INTO public.notifications VALUES (137, 'f284548d-85fa-473c-88e0-0b76a08120b1', 'Cọc thành công', 'Đã thanh toán cọc 50% (4,200đ) cho booking #65. Gia sư sẽ bắt đầu dạy buổi đầu tiên.', 'payment_success', '65', false, '2026-03-22 15:20:35.416362', 'app', NULL, NULL);
INSERT INTO public.notifications VALUES (138, 'e6752adf-18d5-40e3-9dfd-8facb4614695', 'Booking đã được cọc', 'Phụ huynh đã cọc 50% cho booking #65. Bạn có thể bắt đầu dạy.', 'payment_success', '65', false, '2026-03-22 15:20:35.416559', 'app', NULL, NULL);
INSERT INTO public.notifications VALUES (141, 'e6752adf-18d5-40e3-9dfd-8facb4614695', 'New booking request', 'You have a new booking request. Note: Parent requested ''online'' mode, which differs from your preferred ''both'' mode. Payment code: 501DC0668B2F', 'booking_new', '66', false, '2026-03-22 15:29:50.664212', 'app', NULL, NULL);
INSERT INTO public.notifications VALUES (143, 'e6752adf-18d5-40e3-9dfd-8facb4614695', 'Tin nhắn mới', 'Bạn có tin nhắn mới trong cuộc trò chuyện', NULL, NULL, false, '2026-03-22 15:29:50.700173', 'app', NULL, NULL);
INSERT INTO public.notifications VALUES (144, 'f284548d-85fa-473c-88e0-0b76a08120b1', 'Booking declined', 'Gia sư đã từ chối yêu cầu đặt lịch #66', NULL, NULL, false, '2026-03-22 15:29:58.131564', 'app', NULL, NULL);
INSERT INTO public.notifications VALUES (146, 'e6752adf-18d5-40e3-9dfd-8facb4614695', 'New booking request', 'You have a new booking request. Note: Parent requested ''online'' mode, which differs from your preferred ''both'' mode. Payment code: C7FD44E9F78A', 'booking_new', '67', false, '2026-03-23 02:46:08.837916', 'app', NULL, NULL);
INSERT INTO public.notifications VALUES (147, 'e6752adf-18d5-40e3-9dfd-8facb4614695', 'New booking request', 'You have a new booking request. Note: Parent requested ''online'' mode, which differs from your preferred ''both'' mode. Payment code: C7FD44E9F78A', NULL, NULL, false, '2026-03-23 02:46:08.850919', 'app', NULL, NULL);
INSERT INTO public.notifications VALUES (151, 'e6752adf-18d5-40e3-9dfd-8facb4614695', 'New booking request', 'You have a new booking request. Payment code: F777E7C976FF', 'booking_new', '68', false, '2026-03-23 10:30:52.792765', 'app', NULL, NULL);
INSERT INTO public.notifications VALUES (152, 'e6752adf-18d5-40e3-9dfd-8facb4614695', 'New booking request', 'You have a new booking request. Payment code: F777E7C976FF', NULL, NULL, false, '2026-03-23 10:30:52.808163', 'app', NULL, NULL);
INSERT INTO public.notifications VALUES (156, 'f284548d-85fa-473c-88e0-0b76a08120b1', 'Cọc thành công', 'Đã thanh toán cọc 50% (4,200đ) cho booking #68. Gia sư sẽ bắt đầu dạy buổi đầu tiên.', 'payment_success', '68', false, '2026-03-23 10:32:39.911899', 'app', NULL, NULL);
INSERT INTO public.notifications VALUES (157, 'e6752adf-18d5-40e3-9dfd-8facb4614695', 'Booking đã được cọc', 'Phụ huynh đã cọc 50% cho booking #68. Bạn có thể bắt đầu dạy.', 'payment_success', '68', false, '2026-03-23 10:32:39.912044', 'app', NULL, NULL);
INSERT INTO public.notifications VALUES (158, 'f284548d-85fa-473c-88e0-0b76a08120b1', 'Thanh toán hoàn tất', 'Đã thanh toán 50% còn lại (4,200đ) cho booking #68. Booking đã được thanh toán đầy đủ.', 'payment_success', '68', false, '2026-03-23 10:39:38.907128', 'app', NULL, NULL);
INSERT INTO public.notifications VALUES (159, 'e6752adf-18d5-40e3-9dfd-8facb4614695', 'Booking đã thanh toán đầy đủ', 'Booking #68 đã được thanh toán đầy đủ.', 'payment_success', '68', false, '2026-03-23 10:39:38.90724', 'app', NULL, NULL);
INSERT INTO public.notifications VALUES (164, 'f284548d-85fa-473c-88e0-0b76a08120b1', 'Booking accepted', 'Gia sư đã chấp nhận yêu cầu đặt lịch #69', NULL, NULL, true, '2026-03-23 10:47:25.351811', 'app', NULL, NULL);
INSERT INTO public.notifications VALUES (167, 'e6752adf-18d5-40e3-9dfd-8facb4614695', 'Nhắc nhở buổi học', 'Buổi học môn Tiếng Anh sẽ bắt đầu trong 27 phút. Hãy chuẩn bị sẵn sàng!', NULL, NULL, false, '2026-03-25 01:02:36.261867', 'app', NULL, NULL);
INSERT INTO public.notifications VALUES (169, 'e6752adf-18d5-40e3-9dfd-8facb4614695', 'Tin nhắn mới', 'Bạn có tin nhắn mới trong cuộc trò chuyện', NULL, NULL, false, '2026-03-25 12:54:45.139885', 'app', NULL, NULL);
INSERT INTO public.notifications VALUES (93, 'eb79ec08-d7af-46f3-94cf-a818b2136e10', 'Cọc thành công', 'Đã thanh toán cọc 50% (420,000đ) cho booking #60. Gia sư sẽ bắt đầu dạy buổi đầu tiên.', 'payment_success', '60', true, '2026-03-17 12:52:20.740315', 'app', NULL, NULL);
INSERT INTO public.notifications VALUES (88, 'eb79ec08-d7af-46f3-94cf-a818b2136e10', 'Cọc thành công', 'Đã thanh toán cọc 50% (420,000đ) cho booking #59. Gia sư sẽ bắt đầu dạy buổi đầu tiên.', 'payment_success', '59', true, '2026-03-17 12:38:10.067277', 'app', NULL, NULL);
INSERT INTO public.notifications VALUES (90, 'eb79ec08-d7af-46f3-94cf-a818b2136e10', 'Thanh toán hoàn tất', 'Đã thanh toán 50% còn lại (420,000đ) cho booking #59. Booking đã được thanh toán đầy đủ.', 'payment_success', '59', true, '2026-03-17 12:42:11.788877', 'app', NULL, NULL);
INSERT INTO public.notifications VALUES (85, 'eb79ec08-d7af-46f3-94cf-a818b2136e10', 'Thanh toán hoàn tất', 'Đã thanh toán 50% còn lại (420,000đ) cho booking #58. Booking đã được thanh toán đầy đủ.', 'payment_success', '58', true, '2026-03-17 10:25:13.606031', 'app', NULL, NULL);
INSERT INTO public.notifications VALUES (83, 'eb79ec08-d7af-46f3-94cf-a818b2136e10', 'Cọc thành công', 'Đã thanh toán cọc 50% (420,000đ) cho booking #58. Gia sư sẽ bắt đầu dạy buổi đầu tiên.', 'payment_success', '58', true, '2026-03-17 10:24:59.308585', 'app', NULL, NULL);
INSERT INTO public.notifications VALUES (79, 'eb79ec08-d7af-46f3-94cf-a818b2136e10', 'Cọc thành công', 'Đã thanh toán cọc 50% (210,000đ) cho booking #54. Gia sư sẽ bắt đầu dạy buổi đầu tiên.', 'payment_success', '54', true, '2026-03-17 08:39:40.717707', 'app', NULL, NULL);
INSERT INTO public.notifications VALUES (173, 'e6752adf-18d5-40e3-9dfd-8facb4614695', 'Tin nhắn mới', 'Bạn có tin nhắn mới trong cuộc trò chuyện', NULL, NULL, false, '2026-03-25 14:15:18.288345', 'app', NULL, NULL);
INSERT INTO public.notifications VALUES (96, '48a31ca1-cbfd-49d1-845b-d4a923a2c741', 'Cọc thành công', 'Đã thanh toán cọc 50% (4,200đ) cho booking #61. Gia sư sẽ bắt đầu dạy buổi đầu tiên.', 'payment_success', '61', true, '2026-03-17 13:34:17.676897', 'app', NULL, NULL);
INSERT INTO public.notifications VALUES (123, 'e6752adf-18d5-40e3-9dfd-8facb4614695', 'New booking request', 'You have a new booking request. Note: Parent requested ''online'' mode, which differs from your preferred ''both'' mode. Payment code: 5878A546BC25', NULL, NULL, false, '2026-03-19 05:59:11.359854', 'app', NULL, NULL);
INSERT INTO public.notifications VALUES (128, 'e6752adf-18d5-40e3-9dfd-8facb4614695', 'Tin nhắn mới', 'Bạn có tin nhắn mới trong cuộc trò chuyện', NULL, NULL, false, '2026-03-22 11:06:44.307187', 'app', NULL, NULL);
INSERT INTO public.notifications VALUES (129, 'e6752adf-18d5-40e3-9dfd-8facb4614695', 'Tin nhắn mới', 'Bạn có tin nhắn mới trong cuộc trò chuyện', NULL, NULL, false, '2026-03-22 11:07:01.97607', 'app', NULL, NULL);
INSERT INTO public.notifications VALUES (132, 'e6752adf-18d5-40e3-9dfd-8facb4614695', 'New booking request', 'You have a new booking request. Note: Parent requested ''online'' mode, which differs from your preferred ''both'' mode. Payment code: C1AEF179CD83', 'booking_new', '65', false, '2026-03-22 15:11:26.021692', 'app', NULL, NULL);
INSERT INTO public.notifications VALUES (133, 'e6752adf-18d5-40e3-9dfd-8facb4614695', 'New booking request', 'You have a new booking request. Note: Parent requested ''online'' mode, which differs from your preferred ''both'' mode. Payment code: C1AEF179CD83', NULL, NULL, false, '2026-03-22 15:11:26.029638', 'app', NULL, NULL);
INSERT INTO public.notifications VALUES (134, 'e6752adf-18d5-40e3-9dfd-8facb4614695', 'Tin nhắn mới', 'Bạn có tin nhắn mới trong cuộc trò chuyện', NULL, NULL, false, '2026-03-22 15:11:26.055193', 'app', NULL, NULL);
INSERT INTO public.notifications VALUES (139, 'f284548d-85fa-473c-88e0-0b76a08120b1', 'Thanh toán hoàn tất', 'Đã thanh toán 50% còn lại (4,200đ) cho booking #65. Booking đã được thanh toán đầy đủ.', 'payment_success', '65', false, '2026-03-22 15:22:40.652184', 'app', NULL, NULL);
INSERT INTO public.notifications VALUES (140, 'e6752adf-18d5-40e3-9dfd-8facb4614695', 'Booking đã thanh toán đầy đủ', 'Booking #65 đã được thanh toán đầy đủ.', 'payment_success', '65', false, '2026-03-22 15:22:40.652277', 'app', NULL, NULL);
INSERT INTO public.notifications VALUES (142, 'e6752adf-18d5-40e3-9dfd-8facb4614695', 'New booking request', 'You have a new booking request. Note: Parent requested ''online'' mode, which differs from your preferred ''both'' mode. Payment code: 501DC0668B2F', NULL, NULL, false, '2026-03-22 15:29:50.673867', 'app', NULL, NULL);
INSERT INTO public.notifications VALUES (145, 'e6752adf-18d5-40e3-9dfd-8facb4614695', 'Nhắc nhở buổi học', 'Buổi học môn Tiếng Anh sẽ bắt đầu trong 24 phút. Hãy chuẩn bị sẵn sàng!', NULL, NULL, false, '2026-03-23 01:05:08.906207', 'app', NULL, NULL);
INSERT INTO public.notifications VALUES (148, 'e6752adf-18d5-40e3-9dfd-8facb4614695', 'Tin nhắn mới', 'Bạn có tin nhắn mới trong cuộc trò chuyện', NULL, NULL, false, '2026-03-23 02:46:08.881696', 'app', NULL, NULL);
INSERT INTO public.notifications VALUES (149, 'f284548d-85fa-473c-88e0-0b76a08120b1', 'Tin nhắn mới', 'Bạn có tin nhắn mới trong cuộc trò chuyện', NULL, NULL, false, '2026-03-23 02:46:42.513182', 'app', NULL, NULL);
INSERT INTO public.notifications VALUES (150, 'f284548d-85fa-473c-88e0-0b76a08120b1', 'Booking accepted', 'Gia sư đã chấp nhận yêu cầu đặt lịch #67', NULL, NULL, false, '2026-03-23 02:46:42.528262', 'app', NULL, NULL);
INSERT INTO public.notifications VALUES (153, 'e6752adf-18d5-40e3-9dfd-8facb4614695', 'Tin nhắn mới', 'Bạn có tin nhắn mới trong cuộc trò chuyện', NULL, NULL, false, '2026-03-23 10:30:52.858154', 'app', NULL, NULL);
INSERT INTO public.notifications VALUES (154, 'f284548d-85fa-473c-88e0-0b76a08120b1', 'Tin nhắn mới', 'Bạn có tin nhắn mới trong cuộc trò chuyện', NULL, NULL, false, '2026-03-23 10:31:43.020374', 'app', NULL, NULL);
INSERT INTO public.notifications VALUES (155, 'f284548d-85fa-473c-88e0-0b76a08120b1', 'Booking accepted', 'Gia sư đã chấp nhận yêu cầu đặt lịch #68', NULL, NULL, false, '2026-03-23 10:31:43.028825', 'app', NULL, NULL);
INSERT INTO public.notifications VALUES (160, 'e6752adf-18d5-40e3-9dfd-8facb4614695', 'New booking request', 'You have a new booking request. Payment code: 3C995C8AE23F', 'booking_new', '69', false, '2026-03-23 10:45:53.395713', 'app', NULL, NULL);
INSERT INTO public.notifications VALUES (161, 'e6752adf-18d5-40e3-9dfd-8facb4614695', 'New booking request', 'You have a new booking request. Payment code: 3C995C8AE23F', NULL, NULL, false, '2026-03-23 10:45:53.404993', 'app', NULL, NULL);
INSERT INTO public.notifications VALUES (162, 'e6752adf-18d5-40e3-9dfd-8facb4614695', 'Tin nhắn mới', 'Bạn có tin nhắn mới trong cuộc trò chuyện', NULL, NULL, false, '2026-03-23 10:45:53.426915', 'app', NULL, NULL);
INSERT INTO public.notifications VALUES (163, 'f284548d-85fa-473c-88e0-0b76a08120b1', 'Tin nhắn mới', 'Bạn có tin nhắn mới trong cuộc trò chuyện', NULL, NULL, false, '2026-03-23 10:47:25.339704', 'app', NULL, NULL);
INSERT INTO public.notifications VALUES (165, 'f284548d-85fa-473c-88e0-0b76a08120b1', 'Booking đã hết hạn thanh toán', 'Booking #67 đã bị hủy do quá hạn thanh toán 24h.', NULL, NULL, false, '2026-03-24 12:47:03.628549', 'app', NULL, NULL);
INSERT INTO public.notifications VALUES (104, '48a31ca1-cbfd-49d1-845b-d4a923a2c741', 'Tin nhắn mới', 'Bạn có tin nhắn mới trong cuộc trò chuyện', NULL, NULL, true, '2026-03-18 14:52:47.097225', 'app', NULL, NULL);
INSERT INTO public.notifications VALUES (105, '48a31ca1-cbfd-49d1-845b-d4a923a2c741', 'Booking accepted', 'Gia sư đã chấp nhận yêu cầu đặt lịch #62', NULL, NULL, true, '2026-03-18 14:52:47.104742', 'app', NULL, NULL);
INSERT INTO public.notifications VALUES (166, 'e6752adf-18d5-40e3-9dfd-8facb4614695', 'Booking đã hết hạn thanh toán', 'Booking #67 đã bị hủy do phụ huynh không thanh toán trong 24h.', NULL, NULL, false, '2026-03-24 12:47:03.628627', 'app', NULL, NULL);
INSERT INTO public.notifications VALUES (168, 'e6752adf-18d5-40e3-9dfd-8facb4614695', 'Tin nhắn mới', 'Bạn có tin nhắn mới trong cuộc trò chuyện', NULL, NULL, false, '2026-03-25 12:54:06.594125', 'app', NULL, NULL);
INSERT INTO public.notifications VALUES (170, 'e6752adf-18d5-40e3-9dfd-8facb4614695', 'Tin nhắn mới', 'Bạn có tin nhắn mới trong cuộc trò chuyện', NULL, NULL, false, '2026-03-25 12:55:02.104743', 'app', NULL, NULL);
INSERT INTO public.notifications VALUES (171, 'e6752adf-18d5-40e3-9dfd-8facb4614695', 'Tin nhắn mới', 'Bạn có tin nhắn mới trong cuộc trò chuyện', NULL, NULL, false, '2026-03-25 12:55:18.573039', 'app', NULL, NULL);
INSERT INTO public.notifications VALUES (172, 'e6752adf-18d5-40e3-9dfd-8facb4614695', 'Tin nhắn mới', 'Bạn có tin nhắn mới trong cuộc trò chuyện', NULL, NULL, false, '2026-03-25 12:55:38.805958', 'app', NULL, NULL);
INSERT INTO public.notifications VALUES (174, 'e6752adf-18d5-40e3-9dfd-8facb4614695', 'Tin nhắn mới', 'Bạn có tin nhắn mới trong cuộc trò chuyện', NULL, NULL, false, '2026-03-25 14:27:22.742606', 'app', NULL, NULL);
INSERT INTO public.notifications VALUES (111, 'eb79ec08-d7af-46f3-94cf-a818b2136e10', 'Tin nhắn mới', 'Bạn có tin nhắn mới trong cuộc trò chuyện', NULL, NULL, true, '2026-03-18 15:21:18.254243', 'app', NULL, NULL);
INSERT INTO public.notifications VALUES (112, 'eb79ec08-d7af-46f3-94cf-a818b2136e10', 'Tin nhắn mới', 'Bạn có tin nhắn mới trong cuộc trò chuyện', NULL, NULL, true, '2026-03-18 15:22:31.849222', 'app', NULL, NULL);
INSERT INTO public.notifications VALUES (113, 'eb79ec08-d7af-46f3-94cf-a818b2136e10', 'Tin nhắn mới', 'Bạn có tin nhắn mới trong cuộc trò chuyện', NULL, NULL, true, '2026-03-18 15:22:45.175853', 'app', NULL, NULL);
INSERT INTO public.notifications VALUES (106, '48a31ca1-cbfd-49d1-845b-d4a923a2c741', 'Cọc thành công', 'Đã thanh toán cọc 50% (4,200đ) cho booking #62. Gia sư sẽ bắt đầu dạy buổi đầu tiên.', 'payment_success', '62', true, '2026-03-18 14:55:17.920818', 'app', NULL, NULL);
INSERT INTO public.notifications VALUES (121, '48a31ca1-cbfd-49d1-845b-d4a923a2c741', 'Tin nhắn mới', 'Bạn có tin nhắn mới trong cuộc trò chuyện', NULL, NULL, true, '2026-03-18 16:00:18.097137', 'app', NULL, NULL);


--
-- TOC entry 5724 (class 0 OID 279505)
-- Dependencies: 272
-- Data for Name: profilesuspensions; Type: TABLE DATA; Schema: public; Owner: postgres
--



--
-- TOC entry 5726 (class 0 OID 279513)
-- Dependencies: 274
-- Data for Name: promotions; Type: TABLE DATA; Schema: public; Owner: postgres
--

INSERT INTO public.promotions VALUES (9001, 'GIAM10', NULL, 'percent', 10.00, 500000.00, 500000.00, '2026-01-09 11:24:52.119232', '2026-05-09 11:24:52.119232', 100, 5, true, '2026-02-08 11:24:52.119232');
INSERT INTO public.promotions VALUES (9002, 'GIAM50K', NULL, 'fixed', 50000.00, 50000.00, 300000.00, '2026-01-09 11:24:52.119232', '2026-05-09 11:24:52.119232', 50, 2, true, '2026-02-08 11:24:52.119232');
INSERT INTO public.promotions VALUES (1, 'CHAO2026', 'Chào bạn mới - Giảm 10% tối đa 200k', 'percent', 10.00, 1000.00, 1000.00, '2026-01-01 00:00:00', '2027-12-31 00:00:00', 1000, 150, true, '2026-01-10 08:45:17.25673');
INSERT INTO public.promotions VALUES (2, 'GIOVANG', 'Giảm nóng 50k cho đơn từ 300k', 'fixed_amount', 10000.00, 1000.00, 1000.00, '2024-06-01 00:00:00', '2024-08-31 00:00:00', 500, 42, true, '2026-01-10 08:45:17.25673');


--
-- TOC entry 5756 (class 0 OID 280226)
-- Dependencies: 304
-- Data for Name: refreshtokens; Type: TABLE DATA; Schema: public; Owner: postgres
--

INSERT INTO public.refreshtokens VALUES ('82509323-cb85-4118-91ca-cb73d6460e54', 'e8c02290d944e9925da885c5d6ef556859318ed18363b970ed37e6eb45f33464', 'f284548d-85fa-473c-88e0-0b76a08120b1', 'e9dc81df-ab52-4bcb-9779-19dfad2a96bf', '2026-04-01 18:20:00.373201', '2026-03-25 18:20:00.373342', NULL, NULL);
INSERT INTO public.refreshtokens VALUES ('c7be6699-4b4b-4b1c-8957-2cdb87786bee', 'cf7f88928d3658906160e8f91a6dcc11d73f8899ce2ea0ade77205cb0651f6a2', 'f284548d-85fa-473c-88e0-0b76a08120b1', 'a9937d59-4a47-4a72-b8ff-134ebb7606be', '2026-04-01 18:22:16.982894', '2026-03-25 18:22:16.982985', '2026-03-25 18:22:36.334789', 'e479453ed5e5388b825a1d5fb3b68ad0a59be4393ace10489a3b610a5ba77c1c');
INSERT INTO public.refreshtokens VALUES ('b673b366-2de1-4aed-8768-9e34c928bbf1', 'e479453ed5e5388b825a1d5fb3b68ad0a59be4393ace10489a3b610a5ba77c1c', 'f284548d-85fa-473c-88e0-0b76a08120b1', 'a9937d59-4a47-4a72-b8ff-134ebb7606be', '2026-04-01 18:22:36.334788', '2026-03-25 18:22:36.334789', NULL, NULL);
INSERT INTO public.refreshtokens VALUES ('3e3d4301-dd41-4790-bab5-7d88178cf254', 'b431410bd60dd838798350f69ac85f0755787220e7170b915b2ab0d94617a32a', 'f830bfd4-f5b2-487c-8dee-f914ed8d3658', '638a40d4-002f-4264-821e-3460dd4dcf8f', '2026-05-19 08:59:20.324112', '2026-05-12 08:59:20.324223', NULL, NULL);
INSERT INTO public.refreshtokens VALUES ('f9345a22-d0a8-4f39-85ac-01f6fe7cbd2d', '321a1bd33d926298a3c68f7e460ae2c4b7dc2a2a630371747b91f9e6566cd843', 'bba681d9-b28d-4fec-8204-bce6ed6b749a', 'd5065c86-9a58-4880-a471-2d0d479c9589', '2026-05-26 12:00:22.90274', '2026-05-19 12:00:22.902943', NULL, NULL);
INSERT INTO public.refreshtokens VALUES ('8321a694-2bcf-4eba-aa41-ae693a0de0d4', '0e81f15c6d879e414602c394de0a169bfad0e59feb5dce3c7d9e395287c0836c', 'c6a4af05-15a7-4114-a54c-067553ddc677', '67da84ee-665a-4d0a-a18c-3aa5a35f7b81', '2026-05-29 13:48:52.350809', '2026-05-22 13:48:52.350935', NULL, NULL);
INSERT INTO public.refreshtokens VALUES ('c99368e0-195a-444e-a699-b13bd772e99a', '7936995dab05317cdc133dd6c21ecfcf3ec9dfc8bea389e45a95268738ae362f', '74339c05-e30f-4f08-be7c-cfb611597cd9', '72fd499b-698b-4efe-99fb-4c05a6375a41', '2026-06-07 11:32:25.210179', '2026-05-31 11:32:25.210315', NULL, NULL);


--
-- TOC entry 5728 (class 0 OID 279522)
-- Dependencies: 276
-- Data for Name: studentgrades; Type: TABLE DATA; Schema: public; Owner: postgres
--



--
-- TOC entry 5730 (class 0 OID 279530)
-- Dependencies: 278
-- Data for Name: studentprofiles; Type: TABLE DATA; Schema: public; Owner: postgres
--

INSERT INTO public.studentprofiles VALUES ('3eb974d3-0789-4f85-b4b1-e08dec437acc', NULL, '3eb974d3-0789-4f85-b4b1-e08dec437acc', NULL, NULL, NULL, NULL, NULL, '2026-03-06 03:48:52.649988', NULL, NULL, NULL, NULL);
INSERT INTO public.studentprofiles VALUES ('STU-48a31ca1-cbfd-49d1-845b-d4a923a2c741-1', '48a31ca1-cbfd-49d1-845b-d4a923a2c741', NULL, 'Nguyễn Hoàng Ngọc', '2004-05-10', 'THPT Dương Văn Thì', 'Đậu tốt nghiệp với điểm cao', NULL, '2026-03-14 01:00:29.76061', NULL, NULL, NULL, NULL);
INSERT INTO public.studentprofiles VALUES ('STU-48a31ca1-cbfd-49d1-845b-d4a923a2c741-2', '48a31ca1-cbfd-49d1-845b-d4a923a2c741', NULL, 'Nguyễn Viết Hoàng', '2004-05-10', 'THPT Nguyễn Huệ', 'Đạt điểm cao', NULL, '2026-03-14 01:02:19.004763', 'PRS4RP', '2026-03-15 01:14:04.299225', NULL, NULL);
INSERT INTO public.studentprofiles VALUES ('STU-48a31ca1-cbfd-49d1-845b-d4a923a2c741-3', '48a31ca1-cbfd-49d1-845b-d4a923a2c741', NULL, 'Lý Xuân Nguyên', '2014-06-04', 'THCS Trần Quốc Toản', 'Đạt điểm cao', NULL, '2026-03-14 01:03:07.352143', NULL, NULL, NULL, NULL);
INSERT INTO public.studentprofiles VALUES ('STU-48a31ca1-cbfd-49d1-845b-d4a923a2c741-4', '48a31ca1-cbfd-49d1-845b-d4a923a2c741', NULL, 'Lý Xuân Sơn', '2004-05-06', 'THPT Trần Quốc Toản', 'Điểm cao', NULL, '2026-03-14 01:04:00.065314', NULL, NULL, NULL, NULL);
INSERT INTO public.studentprofiles VALUES ('STU-48a31ca1-cbfd-49d1-845b-d4a923a2c741-5', '48a31ca1-cbfd-49d1-845b-d4a923a2c741', NULL, 'Trần Xuân Nguyên', '2004-08-20', 'THPT Quảng Nam', 'Đạt', NULL, '2026-03-14 01:04:38.961119', NULL, NULL, '2026-03-14 01:08:25.661263', NULL);
INSERT INTO public.studentprofiles VALUES ('92325936-6d40-4ca2-a6f7-5d74f85b509d', NULL, NULL, NULL, NULL, NULL, NULL, NULL, '2026-03-13 02:53:47.78654', NULL, NULL, NULL, NULL);
INSERT INTO public.studentprofiles VALUES ('742e2ba3-e975-437e-973d-07f2a9188891', NULL, NULL, NULL, NULL, NULL, NULL, NULL, '2026-03-15 03:58:50.462333', NULL, NULL, NULL, NULL);
INSERT INTO public.studentprofiles VALUES ('433ae1de-b14b-4040-b49c-104bd4196933', NULL, NULL, NULL, NULL, NULL, NULL, NULL, '2026-03-16 16:12:13.476129', NULL, NULL, NULL, NULL);
INSERT INTO public.studentprofiles VALUES ('STU-f284548d-85fa-473c-88e0-0b76a08120b1-4', 'f284548d-85fa-473c-88e0-0b76a08120b1', '6918251f-cf31-45b7-b103-04faed6013d8', 'Lê Thành Công', '2014-06-10', 'Nguyễn Huệ', 'aaaaaaa', NULL, '2026-03-25 12:47:42.408108', NULL, NULL, NULL, NULL);
INSERT INTO public.studentprofiles VALUES ('3efeb8ab-3412-441c-ae16-d250a8b58bcd', NULL, '3efeb8ab-3412-441c-ae16-d250a8b58bcd', NULL, NULL, NULL, NULL, NULL, '2026-03-19 05:56:29.283359', NULL, NULL, NULL, NULL);
INSERT INTO public.studentprofiles VALUES ('b3326bdf-b4a1-4eba-81e5-29925b642a54', NULL, NULL, NULL, NULL, NULL, NULL, NULL, '2026-03-15 14:25:54.203232', NULL, NULL, NULL, NULL);
INSERT INTO public.studentprofiles VALUES ('STU-f284548d-85fa-473c-88e0-0b76a08120b1-1', 'f284548d-85fa-473c-88e0-0b76a08120b1', 'bcc74e02-d7db-4a5e-a8cc-3113732afe52', 'Nguyễn Hoàng Ngọc', '2008-05-30', 'Trường THPT Dương Văn Thì', 'Đạt điểm cao trong kỳ thi cũng như thi tốt nghiệp thành công.', NULL, '2026-03-22 10:28:12.70713', NULL, NULL, NULL, NULL);
INSERT INTO public.studentprofiles VALUES ('a7ed2303-a432-4159-a02a-d597fc136293', 'f284548d-85fa-473c-88e0-0b76a08120b1', 'a7ed2303-a432-4159-a02a-d597fc136293', NULL, NULL, NULL, NULL, NULL, '2026-03-20 11:48:04.712779', NULL, NULL, NULL, NULL);
INSERT INTO public.studentprofiles VALUES ('7c696d73-2b77-4b16-961a-224df3d3d786', NULL, '7c696d73-2b77-4b16-961a-224df3d3d786', NULL, NULL, NULL, NULL, NULL, '2026-03-22 10:44:01.989342', NULL, NULL, NULL, NULL);
INSERT INTO public.studentprofiles VALUES ('STU-f284548d-85fa-473c-88e0-0b76a08120b1-2', 'f284548d-85fa-473c-88e0-0b76a08120b1', '2fbd935d-c116-4792-9b6a-faa0c2b38fb8', 'Lê Thị Huế Trân', '2004-05-25', 'FPT University', 'Good Student', NULL, '2026-03-22 14:51:28.6667', NULL, NULL, NULL, NULL);
INSERT INTO public.studentprofiles VALUES ('STU-f284548d-85fa-473c-88e0-0b76a08120b1-3', 'f284548d-85fa-473c-88e0-0b76a08120b1', 'bb9865e4-92d1-4579-9c34-a74e38676110', 'ưefffffffffffffffwefwefwefwef', '2026-03-22', 'fefwefwf', 'ưefwefwfefwfe', NULL, '2026-03-22 14:53:59.143223', NULL, NULL, '2026-03-22 14:58:59.012969', NULL);
INSERT INTO public.studentprofiles VALUES ('ab4c9cb1-a5b1-4e78-a1eb-6d4db47e8832', NULL, NULL, NULL, NULL, NULL, NULL, NULL, '2026-03-16 16:14:03.2508', NULL, NULL, NULL, NULL);
INSERT INTO public.studentprofiles VALUES ('f830bfd4-f5b2-487c-8dee-f914ed8d3658', NULL, 'f830bfd4-f5b2-487c-8dee-f914ed8d3658', NULL, NULL, NULL, NULL, NULL, '2026-03-22 15:42:48.462861', NULL, NULL, NULL, NULL);
INSERT INTO public.studentprofiles VALUES ('c6a4af05-15a7-4114-a54c-067553ddc677', NULL, 'c6a4af05-15a7-4114-a54c-067553ddc677', NULL, NULL, NULL, NULL, NULL, '2026-05-22 13:48:51.710315', NULL, NULL, NULL, NULL);


--
-- TOC entry 5731 (class 0 OID 279536)
-- Dependencies: 279
-- Data for Name: subjects; Type: TABLE DATA; Schema: public; Owner: postgres
--

INSERT INTO public.subjects VALUES (1, 'Toán Học', NULL);
INSERT INTO public.subjects VALUES (2, 'Tiếng Anh', NULL);
INSERT INTO public.subjects VALUES (3, 'Vật Lý', NULL);
INSERT INTO public.subjects VALUES (4, 'Hóa Học', 'Hóa học phổ thông');
INSERT INTO public.subjects VALUES (5, 'Ngữ văn', 'Literature');
INSERT INTO public.subjects VALUES (7, 'Lịch Sử', NULL);
INSERT INTO public.subjects VALUES (8, 'Địa Lý', NULL);
INSERT INTO public.subjects VALUES (9, 'Tin Học', NULL);
INSERT INTO public.subjects VALUES (10, 'IELTS', NULL);
INSERT INTO public.subjects VALUES (6, 'Sinh Học', NULL);


--
-- TOC entry 5733 (class 0 OID 279542)
-- Dependencies: 281
-- Data for Name: systemalerts; Type: TABLE DATA; Schema: public; Owner: postgres
--



--
-- TOC entry 5735 (class 0 OID 279550)
-- Dependencies: 283
-- Data for Name: systemconfigs; Type: TABLE DATA; Schema: public; Owner: postgres
--

INSERT INTO public.systemconfigs VALUES (1, 'platform_fee_percent', '10', 'Tổng phí sàn (%)', NULL, '2026-01-24 15:30:00.347893');
INSERT INTO public.systemconfigs VALUES (2, 'parent_fee_percent', '5', 'Phí từ phụ huynh (%)', NULL, '2026-01-24 15:30:00.347893');
INSERT INTO public.systemconfigs VALUES (3, 'tutor_fee_percent', '5', 'Phí từ gia sư (%)', NULL, '2026-01-24 15:30:00.347893');
INSERT INTO public.systemconfigs VALUES (4, 'grace_period_hours', '24', 'Thời gian chờ dispute sau buổi học (giờ)', NULL, '2026-01-24 15:30:00.347893');
INSERT INTO public.systemconfigs VALUES (5, 'deposit_percent', '50', 'Tỷ lệ đặt cọc (%)', NULL, '2026-01-24 15:30:00.347893');
INSERT INTO public.systemconfigs VALUES (6, 'warning_level1_threshold', '2', 'Số warning cấp 1 để ẩn profile', NULL, '2026-01-24 15:30:00.347893');
INSERT INTO public.systemconfigs VALUES (7, 'warning_level2_threshold', '1', 'Số warning cấp 2 để ẩn profile', NULL, '2026-01-24 15:30:00.347893');
INSERT INTO public.systemconfigs VALUES (8, 'profile_hidden_days', '7', 'Số ngày ẩn profile', NULL, '2026-01-24 15:30:00.347893');
INSERT INTO public.systemconfigs VALUES (9, 'hidden_to_lock_threshold', '2', 'Số lần ẩn trong 1 tháng để khóa TK', NULL, '2026-01-24 15:30:00.347893');
INSERT INTO public.systemconfigs VALUES (10, 'tutor_late_threshold_minutes', '15', 'Số phút trễ tính là no-show', NULL, '2026-01-24 15:30:00.347893');


--
-- TOC entry 5737 (class 0 OID 279557)
-- Dependencies: 285
-- Data for Name: topuprequests; Type: TABLE DATA; Schema: public; Owner: postgres
--



--
-- TOC entry 5739 (class 0 OID 279563)
-- Dependencies: 287
-- Data for Name: tutoravailability; Type: TABLE DATA; Schema: public; Owner: postgres
--

INSERT INTO public.tutoravailability VALUES (16, '92d26d85-cc6b-4c00-8c16-0c1fe11372e1', 1, '01:00:00', '06:00:00', '2026-03-14 04:11:19.329712', true);
INSERT INTO public.tutoravailability VALUES (43, '47af5dfe-cd4d-41ed-9c19-74eeb18f2005', 1, '19:30:00', '22:00:00', '2026-03-19 03:05:34.116674', true);
INSERT INTO public.tutoravailability VALUES (44, '47af5dfe-cd4d-41ed-9c19-74eeb18f2005', 2, '19:30:00', '22:00:00', '2026-03-19 03:05:56.939369', true);
INSERT INTO public.tutoravailability VALUES (45, '47af5dfe-cd4d-41ed-9c19-74eeb18f2005', 3, '19:30:00', '22:00:00', '2026-03-19 03:06:52.04144', true);
INSERT INTO public.tutoravailability VALUES (46, '47af5dfe-cd4d-41ed-9c19-74eeb18f2005', 4, '19:30:00', '22:00:00', '2026-03-19 03:07:12.259703', true);
INSERT INTO public.tutoravailability VALUES (47, '47af5dfe-cd4d-41ed-9c19-74eeb18f2005', 5, '19:30:00', '22:00:00', '2026-03-19 03:07:30.731668', true);
INSERT INTO public.tutoravailability VALUES (48, '47af5dfe-cd4d-41ed-9c19-74eeb18f2005', 6, '17:00:00', '22:00:00', '2026-03-19 03:07:46.464953', true);
INSERT INTO public.tutoravailability VALUES (49, 'e6752adf-18d5-40e3-9dfd-8facb4614695', 1, '07:00:00', '21:00:00', '2026-03-19 05:28:26.262403', true);
INSERT INTO public.tutoravailability VALUES (50, 'e6752adf-18d5-40e3-9dfd-8facb4614695', 4, '07:00:00', '18:00:00', '2026-03-19 05:28:39.129604', true);
INSERT INTO public.tutoravailability VALUES (51, 'e6752adf-18d5-40e3-9dfd-8facb4614695', 3, '07:00:00', '18:00:00', '2026-03-19 05:29:04.445294', true);
INSERT INTO public.tutoravailability VALUES (52, '8317286a-267a-4da0-82a7-9a0e7fd1d218', 1, '17:30:00', '20:00:00', '2026-03-19 06:21:42.530594', true);
INSERT INTO public.tutoravailability VALUES (54, '8317286a-267a-4da0-82a7-9a0e7fd1d218', 2, '17:30:00', '20:00:00', '2026-03-19 06:22:26.345455', true);
INSERT INTO public.tutoravailability VALUES (55, '8317286a-267a-4da0-82a7-9a0e7fd1d218', 3, '17:30:00', '20:00:00', '2026-03-19 06:22:36.315265', true);
INSERT INTO public.tutoravailability VALUES (62, '92d26d85-cc6b-4c00-8c16-0c1fe11372e1', 2, '01:30:00', '04:00:00', '2026-03-23 05:25:24.336329', true);


--
-- TOC entry 5741 (class 0 OID 279568)
-- Dependencies: 289
-- Data for Name: tutorcertificates; Type: TABLE DATA; Schema: public; Owner: postgres
--

INSERT INTO public.tutorcertificates VALUES ('041f0b8f-0739-44e8-b77c-53e8a15acb59', '47af5dfe-cd4d-41ed-9c19-74eeb18f2005', 'IELTS', 'ielts', 'British Council', 2021, NULL, NULL, 'https://outlxbcgkcuximxydlvq.supabase.co/storage/v1/object/sign/certificate-files/47af5dfe-cd4d-41ed-9c19-74eeb18f2005/4cdcfd27-fbb6-41a3-86d3-52e63f32d092.pdf?token=eyJraWQiOiJzdG9yYWdlLXVybC1zaWduaW5nLWtleV82MTA4N2ZkMC0zYjUwLTQ2NTQtODYwOS1lMGFmYzA3ZTQwNTMiLCJhbGciOiJIUzI1NiJ9.eyJ1cmwiOiJjZXJ0aWZpY2F0ZS1maWxlcy80N2FmNWRmZS1jZDRkLTQxZWQtOWMxOS03NGVlYjE4ZjIwMDUvNGNkY2ZkMjctZmJiNi00MWEzLTg2ZDMtNTJlNjNmMzJkMDkyLnBkZiIsImlhdCI6MTc3MzgxNzM3MSwiZXhwIjoxODA1MzUzMzcxfQ.SqNO2UVW8FKFGbY1T7TLFczR3fiPjwi-qvpXjpXkXLw', '2026-03-18 07:02:51.417799', '2026-03-18 07:02:59.017035', 'pending_review', 'The holder''s name could not be extracted from the certificate image.; The issuing organization could not be extracted from the certificate image.; Could not extract a valid issue date from the certificate image.');
INSERT INTO public.tutorcertificates VALUES ('8ffe41e7-8fbc-4cb7-8afd-3a206aadc119', '47af5dfe-cd4d-41ed-9c19-74eeb18f2005', 'IELTS', 'ielts', 'British Council', 2021, NULL, NULL, 'https://outlxbcgkcuximxydlvq.supabase.co/storage/v1/object/sign/certificate-files/47af5dfe-cd4d-41ed-9c19-74eeb18f2005/2d01521b-6aa5-439c-8892-d8f7318eb4dd.pdf?token=eyJraWQiOiJzdG9yYWdlLXVybC1zaWduaW5nLWtleV82MTA4N2ZkMC0zYjUwLTQ2NTQtODYwOS1lMGFmYzA3ZTQwNTMiLCJhbGciOiJIUzI1NiJ9.eyJ1cmwiOiJjZXJ0aWZpY2F0ZS1maWxlcy80N2FmNWRmZS1jZDRkLTQxZWQtOWMxOS03NGVlYjE4ZjIwMDUvMmQwMTUyMWItNmFhNS00MzljLTg4OTItZDhmNzMxOGViNGRkLnBkZiIsImlhdCI6MTc3MzgxNzM4NiwiZXhwIjoxODA1MzUzMzg2fQ.ek32VRRl5mlDrqheUxASc_1tNliGFhI2p6bAE4-FFzY', '2026-03-18 07:03:06.268453', '2026-03-18 07:03:16.325437', 'pending_review', 'The holder''s name could not be extracted from the certificate image.; The issuing organization could not be extracted from the certificate image.; Could not extract a valid issue date from the certificate image.');
INSERT INTO public.tutorcertificates VALUES ('082f7e15-aab7-4525-92fc-ed140db1a39d', 'e6752adf-18d5-40e3-9dfd-8facb4614695', 'Bằng cử nhân', 'bachelor_degree', 'FPT', 2026, NULL, NULL, 'https://outlxbcgkcuximxydlvq.supabase.co/storage/v1/object/sign/certificate-files/e6752adf-18d5-40e3-9dfd-8facb4614695/22b7ab04-9788-477e-a53c-9fc6c27f6bc0.jpg?token=eyJraWQiOiJzdG9yYWdlLXVybC1zaWduaW5nLWtleV82MTA4N2ZkMC0zYjUwLTQ2NTQtODYwOS1lMGFmYzA3ZTQwNTMiLCJhbGciOiJIUzI1NiJ9.eyJ1cmwiOiJjZXJ0aWZpY2F0ZS1maWxlcy9lNjc1MmFkZi0xOGQ1LTQwZTMtOWRmZC04ZmFjYjQ2MTQ2OTUvMjJiN2FiMDQtOTc4OC00NzdlLWE1M2MtOWZjNmMyN2Y2YmMwLmpwZyIsImlhdCI6MTc3Mzg5Nzg2MywiZXhwIjoxODA1NDMzODYzfQ.vSJ63ckix8_x0RJqHq-hNsE13JIYGaJTXLr599I0QTg', '2026-03-19 05:24:23.543184', '2026-03-19 05:24:25.117971', 'pending_review', 'The holder''s name could not be extracted from the certificate image.; The issuing organization could not be extracted from the certificate image.; Could not extract a valid issue date from the certificate image.');


--
-- TOC entry 5764 (class 0 OID 295625)
-- Dependencies: 312
-- Data for Name: tutorpackagefixedslots; Type: TABLE DATA; Schema: public; Owner: postgres
--



--
-- TOC entry 5762 (class 0 OID 295606)
-- Dependencies: 310
-- Data for Name: tutorpackages; Type: TABLE DATA; Schema: public; Owner: postgres
--

INSERT INTO public.tutorpackages VALUES (1, '47af5dfe-cd4d-41ed-9c19-74eeb18f2005', 'Codex Flexible Smoke', 1, 60, 'smoke test', true, '2026-06-05 14:41:07.741814', '2026-06-05 14:41:07.741814');


--
-- TOC entry 5742 (class 0 OID 279577)
-- Dependencies: 290
-- Data for Name: tutorprofiles; Type: TABLE DATA; Schema: public; Owner: postgres
--

INSERT INTO public.tutorprofiles VALUES ('0c9ad6e4-6512-4e5e-bcfa-9e75fa8aa3c1', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 'draft', false, 0, 0, 0, '2026-03-06 07:11:38.252931', '2026-03-06 07:11:38.313264', NULL, 'free', NULL, false, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, false, NULL, NULL, 0, NULL, NULL, NULL);
INSERT INTO public.tutorprofiles VALUES ('cf457954-3705-41a3-b74f-01c783da0d11', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 'draft', false, 0, 0, 0, '2026-03-10 06:57:35.092345', '2026-03-10 06:57:35.099685', NULL, 'free', NULL, false, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, false, NULL, NULL, 0, NULL, NULL, NULL);
INSERT INTO public.tutorprofiles VALUES ('92d26d85-cc6b-4c00-8c16-0c1fe11372e1', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 'draft', false, 0, 0, 0, '2026-03-10 07:16:14.458476', '2026-03-10 07:16:14.463724', NULL, 'free', NULL, false, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, false, NULL, NULL, 0, NULL, NULL, NULL);
INSERT INTO public.tutorprofiles VALUES ('1af56df5-e9d3-49da-b85a-88c5de28a70e', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 'draft', false, 0, 0, 0, '2026-03-18 17:46:39.517413', '2026-03-18 17:46:39.520342', NULL, 'free', NULL, false, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, false, NULL, NULL, 0, NULL, NULL, NULL);
INSERT INTO public.tutorprofiles VALUES ('e6752adf-18d5-40e3-9dfd-8facb4614695', 'Sinh viên năm cuối ngành Kỹ thuật Phần mềm tại Đại học FPT với tư duy logic vượt trội và
khả năng giải thích các khái niệm Toán – Lý phức tạp một cách dễ hiểu, trực quan. Có
kinh nghiệm thực tế trong việc phân tích vấn đề, xây dựng lộ trình học tập cá nhân hóa
và ứng dụng công nghệ vào hỗ trợ giảng dạy. Đam mê truyền cảm hứng cho học sinh yêu
thích Toán và Vật Lý thông qua phương pháp tư duy hệ thống.', 'Sinh viên năm cuối ngành Kỹ thuật Phần mềm tại Đại học FPT với tư duy logic vượt trội và
khả năng giải thích các khái niệm Toán – Lý phức tạp một cách dễ hiểu, trực quan.', 'https://outlxbcgkcuximxydlvq.supabase.co/storage/v1/object/sign/video-introduction/e6752adf-18d5-40e3-9dfd-8facb4614695/6e567de9-117e-4332-8ca7-b3c62a390c8e.mp4?token=eyJraWQiOiJzdG9yYWdlLXVybC1zaWduaW5nLWtleV82MTA4N2ZkMC0zYjUwLTQ2NTQtODYwOS1lMGFmYzA3ZTQwNTMiLCJhbGciOiJIUzI1NiJ9.eyJ1cmwiOiJ2aWRlby1pbnRyb2R1Y3Rpb24vZTY3NTJhZGYtMThkNS00MGUzLTlkZmQtOGZhY2I0NjE0Njk1LzZlNTY3ZGU5LTExN2UtNDMzMi04Y2E3LWIzYzYyYTM5MGM4ZS5tcDQiLCJpYXQiOjE3NzM4OTcyNDQsImV4cCI6MTgwNTQzMzI0NH0.rY_clU92A05ClhEQRVeaNXDlcNV4E7WC7RRQDDxPFaQ', 'Cử Nhân Kỹ Thuật Phần Mềm', 4, 3.5, 'Product Owner & Developer — Tutora (Nền tảng Gia sư K-12)
12/2025 – Nay
● Sáng lập và dẫn dắt nền tảng kết nối gia sư K-12 với học sinh — am hiểu sâu sắc nhu cầu học
tập của phụ huynh và học sinh.
● Top 3 chương trình FPT-U Pitch Day | Venture Builder — chứng minh khả năng lãnh đạo và tư
duy chiến lược.
Business Analyst (Thử việc) — Axon Active
10/2025 – Nay
● Rèn luyện kỹ năng phân tích vấn đề, trình bày logic và giao tiếp hiệu quả với nhiều đối tượng—kỹ năng cốt lõi trong giảng dạy', 'hochiminh', 'tp-thu-duc', 'Online', 'active', true, 0, 0, 0, '2026-03-19 05:12:56.111722', '2026-03-23 02:49:04.011098', NULL, 'free', NULL, false, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, false, NULL, NULL, 0, NULL, NULL, NULL);
INSERT INTO public.tutorprofiles VALUES ('c1ace8bf-425f-4650-8b19-f839454e1b58', NULL, 'Gia sư tiếng Tin học', NULL, NULL, NULL, NULL, NULL, 'hochiminh', 'binh-thanh', 'Online', 'draft', false, 0, 0, 0, '2026-03-19 05:57:56.612295', '2026-03-19 06:07:06.326585', NULL, 'free', NULL, false, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, false, NULL, NULL, 0, NULL, NULL, NULL);
INSERT INTO public.tutorprofiles VALUES ('8317286a-267a-4da0-82a7-9a0e7fd1d218', NULL, 'Có kinh nghiệm ôn thi lớp 12 cho học sinh.', NULL, NULL, NULL, NULL, NULL, 'hochiminh', 'tp-thu-duc', 'Online', 'draft', false, 0, 0, 0, '2026-03-19 06:20:23.18913', '2026-03-19 06:25:18.271626', NULL, 'free', NULL, false, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, false, NULL, NULL, 0, NULL, NULL, NULL);
INSERT INTO public.tutorprofiles VALUES ('47af5dfe-cd4d-41ed-9c19-74eeb18f2005', 'Phương pháp giảng dạy: trò chuyện với học viên để hiểu học viên đang cần gì và thiết kế phương thức dạy phù hợp', 'Gia sư tiếng Anh với 5 năm kinh nghiệm làm việc với người nước ngoài và 1 năm đứng lớp trực tiếp', 'https://outlxbcgkcuximxydlvq.supabase.co/storage/v1/object/sign/video-introduction/47af5dfe-cd4d-41ed-9c19-74eeb18f2005/7231c339-d418-43d9-a119-10c6a0ddcd2a.mov?token=eyJraWQiOiJzdG9yYWdlLXVybC1zaWduaW5nLWtleV82MTA4N2ZkMC0zYjUwLTQ2NTQtODYwOS1lMGFmYzA3ZTQwNTMiLCJhbGciOiJIUzI1NiJ9.eyJ1cmwiOiJ2aWRlby1pbnRyb2R1Y3Rpb24vNDdhZjVkZmUtY2Q0ZC00MWVkLTljMTktNzRlZWIxOGYyMDA1LzcyMzFjMzM5LWQ0MTgtNDNkOS1hMTE5LTEwYzZhMGRkY2QyYS5tb3YiLCJpYXQiOjE3NzM4OTg3OTYsImV4cCI6MTgwNTQzNDc5Nn0.sK7u4ySFzEqa-iCOeV4ntFN28gn8S2vQPjV5bJPEZmw', 'Cử nhân Kinh doanh quốc tế - Học viện Ngoại giao', 4, 3.44, 'Có 5 năm kinh nghiệm hỗ trợ người nước ngoài giảng dạy và trực tiếp đứng lớp cho các học viên từ mẫu giáo đến học viên đã đi làm', 'hochiminh', 'tan-phu', 'Online', 'active', true, 0, 0, 0, '2026-03-08 09:41:50.056418', '2026-03-25 11:54:59.724735', NULL, 'free', 0.00, false, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, false, NULL, NULL, 0, NULL, NULL, NULL);
INSERT INTO public.tutorprofiles VALUES ('bba681d9-b28d-4fec-8204-bce6ed6b749a', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 'draft', false, 0, 0, 0, '2026-05-19 12:00:21.919863', '2026-05-19 19:00:22.52048', NULL, 'free', NULL, false, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, false, NULL, NULL, 0, NULL, NULL, NULL);
INSERT INTO public.tutorprofiles VALUES ('74339c05-e30f-4f08-be7c-cfb611597cd9', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 'draft', false, 0, 0, 0, '2026-05-31 11:32:24.16977', '2026-05-31 18:32:24.843785', NULL, 'free', NULL, false, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, false, NULL, NULL, 0, NULL, NULL, NULL);
INSERT INTO public.tutorprofiles VALUES ('c0112fa7-d3c2-44d4-a013-f038975d2411', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 'draft', false, 0, 0, 0, '2026-06-05 14:39:55.311648', '2026-06-05 14:39:55.319237', NULL, 'free', NULL, false, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, false, NULL, NULL, 0, NULL, NULL, NULL);


--
-- TOC entry 5760 (class 0 OID 295578)
-- Dependencies: 308
-- Data for Name: tutorsubjectgradeprices; Type: TABLE DATA; Schema: public; Owner: postgres
--



--
-- TOC entry 5743 (class 0 OID 279600)
-- Dependencies: 291
-- Data for Name: tutorsubscriptions; Type: TABLE DATA; Schema: public; Owner: postgres
--



--
-- TOC entry 5745 (class 0 OID 279607)
-- Dependencies: 293
-- Data for Name: users; Type: TABLE DATA; Schema: public; Owner: postgres
--

INSERT INTO public.users VALUES ('162ae810-fa05-44fd-9e46-71a216428851', NULL, '100000.NMrvg1O3m4SL9gA+rTgwbw==.LfHCt91bDrTiwE/tdxzEYz27fzYmsKIlk9HbtSHYS+A=', 'lethanhcong.work@gmail.com', '0772017159', true, NULL, 'Lê Thành Công', NULL, NULL, NULL, NULL, NULL, NULL, NULL, false, 1, true, NULL, '2026-03-05 15:37:43.277994', NULL, NULL, NULL, 0, 'Parent', NULL, true, NULL, NULL, false, NULL, false, NULL);
INSERT INTO public.users VALUES ('3eb974d3-0789-4f85-b4b1-e08dec437acc', NULL, '100000.HMBfp3QI86fwN/ksKYHr+Q==.lxz/5m075PhSm/SiYzSdYtAMR/6PsU4T+b93+VE2lQc=', 'phatdt010104@gmail.com', '0363286123', true, NULL, 'Phát Dương', NULL, NULL, NULL, NULL, NULL, NULL, NULL, false, 1, true, NULL, '2026-03-06 03:48:52.64626', NULL, NULL, NULL, 0, 'Student', NULL, true, NULL, NULL, false, NULL, false, NULL);
INSERT INTO public.users VALUES ('0c9ad6e4-6512-4e5e-bcfa-9e75fa8aa3c1', NULL, '100000.T6WfUq3TecVqJbFBF756eA==.x4FVcPC9pgrBDiHI60eroZWVK39mrJ6v+lN04683w00=', 'an0902629323@gmail.com', '0348568001', true, NULL, 'Nguyễn văn An', NULL, NULL, NULL, NULL, NULL, NULL, NULL, false, 1, true, NULL, '2026-03-06 07:11:38.251788', NULL, NULL, NULL, 0, 'Tutor', NULL, true, NULL, NULL, false, NULL, false, NULL);
INSERT INTO public.users VALUES ('c84f0f5b-f7d0-41fc-91ad-f12a4132c8db', NULL, '100000.7X0jNvpbCN+enZ4rHy4lAg==.LipezSrYOqf3UtDEvRnBOvxkk/uj6wcyqmNa+pLK+yc=', 'liebmann.michael@gmail.com', '964433714', true, NULL, 'Michael Liebmann', NULL, NULL, NULL, NULL, NULL, NULL, NULL, false, 1, true, NULL, '2026-03-06 11:09:19.505435', NULL, NULL, NULL, 0, 'Parent', NULL, true, NULL, NULL, false, NULL, false, NULL);
INSERT INTO public.users VALUES ('cf457954-3705-41a3-b74f-01c783da0d11', NULL, '100000.zzMfiuPm4bbOPpB3fPr0pQ==.BzcWxVQehjYfSGmVQT12tdj2Wucum/+cE07qeg0HiJk=', 'duongthanhphat010104@gmail.com', '0363286919', true, NULL, 'Phát Dương', NULL, NULL, NULL, NULL, NULL, NULL, NULL, false, 1, true, NULL, '2026-03-10 06:57:35.091931', NULL, NULL, NULL, 0, 'Parent', NULL, true, NULL, NULL, false, NULL, false, NULL);
INSERT INTO public.users VALUES ('90e466f0-c452-4798-b896-efac322fa1ec', NULL, '100000.P6GPrFXnif0MnIF4pqRx9A==.N1ZSjzKHguOsfwi3kwhWIdSYvG7pGmM7jHWD+7zxxmw=', 'hoangnvse183852@fpt.edu.vn', '0908833439', true, NULL, 'Parent02', NULL, NULL, NULL, NULL, NULL, NULL, NULL, false, 1, true, NULL, '2026-03-15 14:49:39.648783', NULL, NULL, NULL, 0, 'Parent', NULL, true, NULL, NULL, false, NULL, false, NULL);
INSERT INTO public.users VALUES ('48a31ca1-cbfd-49d1-845b-d4a923a2c741', NULL, '100000.NdwprQGJRr0R5WepodLNWA==.vyMQyiLurfdgvs3ttXplvZHg/Vi9SYLN1dtIjSrR5E8=', 'parent@gmail.com', '0906853431', true, NULL, 'Parent01', NULL, NULL, NULL, NULL, NULL, NULL, NULL, false, 1, true, NULL, '2026-03-14 00:59:21.225073', NULL, NULL, NULL, 0, 'Parent', NULL, true, 'XT6KMS', '2026-03-16 04:06:21.508903', false, NULL, false, NULL);
INSERT INTO public.users VALUES ('92d26d85-cc6b-4c00-8c16-0c1fe11372e1', NULL, '100000.7/pXunZK/FSYljNR6jZqvg==.vuh4WW/NC/wbbAbdK46khtv5bBFKY8jTIjSkE5bA7wI=', 'phatdtse183374@fpt.edu.vn', '0363286532', true, NULL, 'Duong Thanh Phat', '2004-01-01', 'NAM', NULL, 'ẤP XÓM GÒ BÀ KÝ, LONG PHƯỚC, LONG THÀNH, ĐỒNG NAI', '075204017949', '92d26d85-cc6b-4c00-8c16-0c1fe11372e1/cccd_front.jpg', '92d26d85-cc6b-4c00-8c16-0c1fe11372e1/cccd_back.jpg', true, 1, true, NULL, '2026-03-10 07:16:14.458452', '{"OcrResult":{"id":"075204017949","name":"D\u01AF\u01A0NG TH\u00C0NH PH\u00C1T","dob":"01/01/2004","home":"H\u00D2A LONG TH\u00C0NH PH\u1ED0 B\u00C0 R\u1ECAA  B\u00C0 R\u1ECAA -","address":"\u1EA4P X\u00D3M G\u00D2 B\u00C0 K\u00DD, LONG PH\u01AF\u1EDAC, LONG TH\u00C0NH, \u0110\u1ED2NG NAI","type_new":"cccd_chip_front","sex":"NAM","id_prob":"99.32"},"VerifiedAt":"2026-03-16T16:14:14.402414Z"}', NULL, NULL, 0, 'Tutor', NULL, true, NULL, NULL, true, NULL, false, NULL);
INSERT INTO public.users VALUES ('eb79ec08-d7af-46f3-94cf-a818b2136e10', NULL, '100000.b5+4PPQ6Je7LAv1fQGMn1A==.HQoofSFRfUPf3YEXa5ijOn7yyEHWFruixSwtWtqgtP4=', 'thanhcongle666@gmail.com', '7772017159', true, NULL, 'Lê Thành Công', NULL, NULL, NULL, NULL, NULL, NULL, NULL, false, 1, true, NULL, '2026-03-16 16:25:17.898508', NULL, NULL, NULL, 0, 'Parent', NULL, true, NULL, NULL, false, NULL, false, NULL);
INSERT INTO public.users VALUES ('2fbd935d-c116-4792-9b6a-faa0c2b38fb8', 'sadasdasdasdasd528', '100000.4H+g6WRbyDyrSUxMsZOKuA==.s4r5Bqn/raGhXsypWFh9YSNKAM3odGSZVPOaVvgykPE=', '2fbd935d-c116-4792-9b6a-faa0c2b38fb8@noemail.agora.local', NULL, false, NULL, 'sadasdasdasdasd', NULL, NULL, NULL, NULL, NULL, NULL, NULL, false, 1, false, NULL, '2026-03-22 14:51:28.657716', NULL, NULL, NULL, 0, 'Student', NULL, true, NULL, NULL, false, NULL, false, NULL);
INSERT INTO public.users VALUES ('7c696d73-2b77-4b16-961a-224df3d3d786', NULL, '100000.nLNYAfWhNR+114XGcDrwZQ==.3NqEys0QAggQAbchez0oRvNYe8QEPvQIigXx2VDaxCo=', 'nguyenviethoanglop9a3@gmail.com', '0605421973', true, NULL, 'Lê Thành Công(test hiện tên người nhắn)', '2002-05-18', 'Male', 'https://outlxbcgkcuximxydlvq.supabase.co/storage/v1/object/sign/avatars/7c696d73-2b77-4b16-961a-224df3d3d786/3bccfc6a-c593-4206-a92f-81e80c3759ca.webp?token=eyJraWQiOiJzdG9yYWdlLXVybC1zaWduaW5nLWtleV82MTA4N2ZkMC0zYjUwLTQ2NTQtODYwOS1lMGFmYzA3ZTQwNTMiLCJhbGciOiJIUzI1NiJ9.eyJ1cmwiOiJhdmF0YXJzLzdjNjk2ZDczLTJiNzctNGIxNi05NjFhLTIyNGRmM2QzZDc4Ni8zYmNjZmM2YS1jNTkzLTQyMDYtYTkyZi04MWU4MGMzNzU5Y2Eud2VicCIsImlhdCI6MTc3NDQ0OTAxNCwiZXhwIjoxODA1OTg1MDE0fQ.koffYPWRykuZzRAq6QqBFr4oNxrv6WR7QHhjKFx4-Vo', 'aaaaa', NULL, NULL, NULL, false, 1, true, NULL, '2026-03-22 10:44:01.989207', NULL, NULL, NULL, 0, 'Student', NULL, true, NULL, NULL, false, NULL, false, NULL);
INSERT INTO public.users VALUES ('1af56df5-e9d3-49da-b85a-88c5de28a70e', NULL, '100000.VTbNW+YD9i6G6c5K46JXQw==.EJseoUNTYIu+zs02FLU9Q0cOzjt7NKDjlJXbBlC0Ixw=', 'thanhcong.work@gmail.com', '0772017154', true, NULL, 'Lê Thành Công', '2004-06-15', 'NAM', NULL, '83/10 ĐƯỜNG 385, TĂNG NHƠN PHÚ A, TP THỦ ĐỨC, TP HỒ CHÍ MINH', '038204000471', '1af56df5-e9d3-49da-b85a-88c5de28a70e/cccd_front.jpg', '1af56df5-e9d3-49da-b85a-88c5de28a70e/cccd_back.jpg', true, 1, true, NULL, '2026-03-18 17:46:39.517388', '{"OcrResult":{"id":"038204000471","name":"L\u00CA TH\u00C0NH C\u00D4NG","dob":"15/06/2004","home":"THANH S\u01A0N, TH\u1ECA X\u00C3 NGHI S\u01A0N, THANH H\u00D3A","address":"83/10 \u0110\u01AF\u1EDCNG 385, T\u0102NG NH\u01A0N PH\u00DA A, TP TH\u1EE6 \u0110\u1EE8C, TP H\u1ED2 CH\u00CD MINH","type_new":"cccd_chip_front","sex":"NAM","id_prob":"99.11"},"VerifiedAt":"2026-03-18T17:47:13.7411264Z"}', NULL, NULL, 0, 'Tutor', NULL, true, NULL, NULL, false, NULL, false, NULL);
INSERT INTO public.users VALUES ('47af5dfe-cd4d-41ed-9c19-74eeb18f2005', NULL, '100000.I/4bOH/8DUhIs9XUMMX8TA==.vzs+li7C12JHRA5ZqnvHFXUgefeuouG+ZrEGiAkXyOk=', 'chumyanh0809ch@gmail.com', '0963846802', true, NULL, 'Chu My Anh', '2003-09-30', 'NỮ', 'https://outlxbcgkcuximxydlvq.supabase.co/storage/v1/object/sign/tutor-avatars/47af5dfe-cd4d-41ed-9c19-74eeb18f2005/e31a7536-8e58-4500-8443-cb8098267c47.jpg?token=eyJraWQiOiJzdG9yYWdlLXVybC1zaWduaW5nLWtleV82MTA4N2ZkMC0zYjUwLTQ2NTQtODYwOS1lMGFmYzA3ZTQwNTMiLCJhbGciOiJIUzI1NiJ9.eyJ1cmwiOiJ0dXRvci1hdmF0YXJzLzQ3YWY1ZGZlLWNkNGQtNDFlZC05YzE5LTc0ZWViMThmMjAwNS9lMzFhNzUzNi04ZTU4LTQ1MDAtODQ0My1jYjgwOTgyNjdjNDcuanBnIiwiaWF0IjoxNzczODkwMDI2LCJleHAiOjE4MDU0MjYwMjZ9.YsZDIdyyYkL7qp92IFAhMcymMQG4WpAuVrtae74M6_c', '7 NGUYỄN CHÁNH TỔ 2, BÀ TRIỆU, TP NAM ĐỊNH, NAM ĐỊNH', '036303011337', '47af5dfe-cd4d-41ed-9c19-74eeb18f2005/cccd_front.jpg', '47af5dfe-cd4d-41ed-9c19-74eeb18f2005/cccd_back.jpg', true, 1, true, NULL, '2026-03-08 09:41:50.056401', '{"OcrResult":{"id":"036303011337","name":"CHU M\u1EF8 ANH","dob":"30/09/2003","home":"NGH\u0128A CH\u00C2U, NGH\u0128A H\u01AFNG, NAM \u0110\u1ECANH","address":"7 NGUY\u1EC4N CH\u00C1NH T\u1ED4 2, B\u00C0 TRI\u1EC6U, TP NAM \u0110\u1ECANH, NAM \u0110\u1ECANH","type_new":"cccd_chip_front","sex":"N\u1EEE","id_prob":"98.85"},"VerifiedAt":"2026-03-19T03:12:04.9063904Z"}', NULL, NULL, 0, 'Tutor', NULL, true, NULL, NULL, true, NULL, false, NULL);
INSERT INTO public.users VALUES ('bb9865e4-92d1-4579-9c34-a74e38676110', 'uefffffffffffff9356', '100000.dFXY2WklY1iL0fyOW+MsyA==.F7m1PqW25X34oXkxOgEkPjwRRVU9eC+Xw5zxnqmaypk=', 'bb9865e4-92d1-4579-9c34-a74e38676110@noemail.agora.local', NULL, false, NULL, 'ưefffffffffffffffwefwefwefwef', NULL, NULL, NULL, NULL, NULL, NULL, NULL, false, 1, false, NULL, '2026-03-22 14:53:59.139734', NULL, NULL, NULL, 0, 'Student', NULL, true, NULL, NULL, false, NULL, false, NULL);
INSERT INTO public.users VALUES ('217ca8dc-8932-497f-b07b-b99af66efba8', NULL, '100000.rcUse5yNFQDg6IKL6slcUg==.o2Ws8Hh0FpzqNht9JW76+I/AwT1lrGSXf9nIWhBNtWI=', 'unclek293@gmail.com', '0986788854', true, NULL, 'NGUYEN VAN A', NULL, NULL, NULL, NULL, NULL, NULL, NULL, false, 1, true, NULL, '2026-03-19 05:53:50.124849', NULL, NULL, NULL, 0, 'Parent', NULL, true, 'MGG4FX', '2026-03-20 05:54:52.788157', false, NULL, false, NULL);
INSERT INTO public.users VALUES ('e6752adf-18d5-40e3-9dfd-8facb4614695', NULL, '100000.f7D7jyqpbP1WHMXQKz4I5w==.L8YT7JTpYpdkrphZzI7ly1iyBYLf+Fe32NYNYgY6Oio=', 'lequockhanh293@gmail.com', '0986788853', true, NULL, 'Quốc khánh', '2003-09-02', 'NAM', 'https://outlxbcgkcuximxydlvq.supabase.co/storage/v1/object/sign/tutor-avatars/e6752adf-18d5-40e3-9dfd-8facb4614695/95dfc781-5407-4a6f-a44c-95052fb00895.jpg?token=eyJraWQiOiJzdG9yYWdlLXVybC1zaWduaW5nLWtleV82MTA4N2ZkMC0zYjUwLTQ2NTQtODYwOS1lMGFmYzA3ZTQwNTMiLCJhbGciOiJIUzI1NiJ9.eyJ1cmwiOiJ0dXRvci1hdmF0YXJzL2U2NzUyYWRmLTE4ZDUtNDBlMy05ZGZkLThmYWNiNDYxNDY5NS85NWRmYzc4MS01NDA3LTRhNmYtYTQ0Yy05NTA1MmZiMDA4OTUuanBnIiwiaWF0IjoxNzczODk3NzI4LCJleHAiOjE4MDU0MzM3Mjh9.JW4sgCzpBUIAXwtJKg26krq1abkk4G7MfogL-Vuv458', '19/6 NGUYỄN CỬU ĐÀM, TÂN SƠN NHÌ, TÂN PHÚ, TP HỒ CHÍ MINH', '079203027772', 'e6752adf-18d5-40e3-9dfd-8facb4614695/cccd_front.jpg', 'e6752adf-18d5-40e3-9dfd-8facb4614695/cccd_back.jpg', true, 1, true, NULL, '2026-03-19 05:12:56.111478', '{"OcrResult":{"id":"079203027772","name":"L\u00CA QU\u1ED0C KH\u00C1NH","dob":"02/09/2003","home":"PH\u01AF\u1EDAC \u0110\u00D4NG, C\u1EA6N \u0110\u01AF\u1EDAC, LONG AN","address":"19/6 NGUY\u1EC4N C\u1EECU \u0110\u00C0M, T\u00C2N S\u01A0N NH\u00CC, T\u00C2N PH\u00DA, TP H\u1ED2 CH\u00CD MINH","type_new":"cccd_chip_front","sex":"NAM","id_prob":"98.89"},"VerifiedAt":"2026-03-19T05:28:03.8717754Z"}', NULL, NULL, 0, 'Tutor', NULL, true, NULL, NULL, true, NULL, false, NULL);
INSERT INTO public.users VALUES ('3efeb8ab-3412-441c-ae16-d250a8b58bcd', NULL, '100000.EieUjMSVNsEV+5cXcEm39A==.AqckwewYnLyXJnB7BJ2s8rNiFgKdqCZs/GLFk8SkQc4=', 'student@gmail.com', '123123123', true, NULL, 'student', NULL, NULL, NULL, NULL, NULL, NULL, NULL, false, 1, true, NULL, '2026-03-19 05:56:29.282766', NULL, NULL, NULL, 0, 'Student', NULL, true, NULL, NULL, false, NULL, false, NULL);
INSERT INTO public.users VALUES ('c1ace8bf-425f-4650-8b19-f839454e1b58', NULL, '100000.8Mbfd4YgBgftriXGN8wukg==.1Ti3FrX1ALsE1FWCQnKn5SNaq8GIIdy1E/dsLSSOiPw=', 'thienlegia2104@gmail.com', '0901921082', true, NULL, 'Lê Gia Thiên', NULL, NULL, NULL, NULL, NULL, NULL, NULL, false, 1, true, NULL, '2026-03-19 05:57:56.612281', NULL, NULL, NULL, 0, 'Tutor', NULL, true, NULL, NULL, true, NULL, false, NULL);
INSERT INTO public.users VALUES ('8317286a-267a-4da0-82a7-9a0e7fd1d218', NULL, '100000.6kSegkCl6C+72u9sWD4rrw==.xg2KNLiZQq0f2MYb1+unnxMttHOPc/hQQ7Wk/R7o3eY=', 'nguyenhattruong6200@gmail.com', '0786315267', true, NULL, 'Nguyễn Nhật Trường', NULL, NULL, NULL, NULL, NULL, NULL, NULL, false, 1, true, NULL, '2026-03-19 06:20:23.189086', NULL, NULL, NULL, 0, 'Tutor', NULL, true, NULL, NULL, true, NULL, false, NULL);
INSERT INTO public.users VALUES ('f284548d-85fa-473c-88e0-0b76a08120b1', NULL, '100000.nLNYAfWhNR+114XGcDrwZQ==.3NqEys0QAggQAbchez0oRvNYe8QEPvQIigXx2VDaxCo=', 'hoangnv10052004@gmail.com', '0906853439', true, NULL, 'Lý Thị Thùy Dương', NULL, NULL, NULL, NULL, NULL, NULL, NULL, false, 1, true, NULL, '2026-03-20 11:48:22.968251', NULL, NULL, NULL, 0, 'Parent', NULL, true, NULL, NULL, false, NULL, false, NULL);
INSERT INTO public.users VALUES ('a7ed2303-a432-4159-a02a-d597fc136293', NULL, '100000.YpUUj7eccOObrISS9oyoMw==.7Yx5bP4EzZiMyPsi/EwW73IMtovURhpWMwuPw8dG0UI=', 'nguyenviethoanglop10a12@gmail.com', '0775743304', true, NULL, 'Nguyễn Viết Hoàng', NULL, NULL, NULL, NULL, NULL, NULL, NULL, false, 1, true, NULL, '2026-03-20 11:48:04.712591', NULL, NULL, NULL, 0, 'Student', NULL, true, NULL, NULL, false, NULL, false, NULL);
INSERT INTO public.users VALUES ('bcc74e02-d7db-4a5e-a8cc-3113732afe52', 'nguyenhoangngoc1179', '100000.35iPdvRxKGt/yDL6x9zgHQ==.ToOVp9zvLXHNY1MbbaULOFckUcFQ9Cns1KdnRPVDON4=', 'bcc74e02-d7db-4a5e-a8cc-3113732afe52@noemail.agora.local', NULL, false, NULL, 'Nguyễn Hoàng Ngọc', NULL, NULL, NULL, NULL, NULL, NULL, NULL, false, 1, false, NULL, '2026-03-22 10:28:12.643477', NULL, NULL, NULL, 0, 'Student', NULL, true, NULL, NULL, false, NULL, false, NULL);
INSERT INTO public.users VALUES ('6918251f-cf31-45b7-b103-04faed6013d8', 'lethanhcong2574', '100000.XX4geAUDNyZ4uUQegkxbmw==.JLofMXZOGeCgVaSk0hKHkoPqh9SH7yEQq00NrqEegxQ=', '6918251f-cf31-45b7-b103-04faed6013d8@noemail.agora.local', NULL, false, NULL, 'Lê Thành Công', NULL, NULL, NULL, NULL, NULL, NULL, NULL, false, 1, false, NULL, '2026-03-25 12:47:42.387721', NULL, NULL, NULL, 0, 'Student', NULL, true, NULL, NULL, false, NULL, false, NULL);
INSERT INTO public.users VALUES ('f830bfd4-f5b2-487c-8dee-f914ed8d3658', NULL, '100000.6y275YkW9W6EXTg2drB/qg==.5TekKrGO3awB970THYafuMUeaHDyfziDIYEJcpxP6Y4=', 'admin@gmail.com', '0554216683', true, NULL, 'Admin', NULL, NULL, NULL, NULL, NULL, NULL, NULL, false, 1, true, NULL, '2026-03-22 15:42:48.462768', NULL, NULL, NULL, 0, 'Admin', NULL, true, NULL, NULL, false, NULL, false, NULL);
INSERT INTO public.users VALUES ('bba681d9-b28d-4fec-8204-bce6ed6b749a', NULL, '100000.D8NZP+ngmY6GKZxJSoJiJg==.lnpytiiOJ33GXxM7Vu3LivRNYiR98V4KnTiybs8wnT4=', 'cong123@gmail.com', '0987654329', true, NULL, 'string', NULL, NULL, NULL, NULL, NULL, NULL, NULL, false, 1, true, NULL, '2026-05-19 12:00:21.918479', NULL, NULL, NULL, 0, 'Tutor', NULL, true, NULL, NULL, false, NULL, false, NULL);
INSERT INTO public.users VALUES ('c6a4af05-15a7-4114-a54c-067553ddc677', NULL, '100000.aghN38yhCTfCFhuVmWh/qg==.DpAOBa3LjJd6jO4tsGFITlN/svVD8t6/QvyMbKdUzf4=', 'hihihihi@gmail.com', '1254789630', true, NULL, 'Nguyễn Viết Hoàng', NULL, NULL, NULL, NULL, NULL, NULL, NULL, false, 1, true, NULL, '2026-05-22 13:48:51.709891', NULL, NULL, NULL, 0, 'Student', NULL, true, NULL, NULL, false, NULL, false, NULL);
INSERT INTO public.users VALUES ('74339c05-e30f-4f08-be7c-cfb611597cd9', NULL, '100000.qTHKsjZeSIBcv19KmjTdBQ==.+j7tWyXXw2qmv17Icx7VAu+2H/jf77OKwzuDa+/wMOo=', 'thanhcongle6667@gmail.com', '0772017150', true, NULL, 'Lê Thành Công', NULL, NULL, NULL, NULL, NULL, NULL, NULL, false, 1, true, NULL, '2026-05-31 11:32:24.168832', NULL, NULL, NULL, 0, 'Tutor', NULL, true, NULL, NULL, true, NULL, false, NULL);
INSERT INTO public.users VALUES ('c0112fa7-d3c2-44d4-a013-f038975d2411', NULL, '100000.VX6Z7FQC702sHxJjj4J8OQ==.l/z/pUiFb1DmZT50pHhY5VUjsco7VwTzeieSt02m0ew=', 'codex.tutor.1780645195@test.local', NULL, false, NULL, 'Codex Tutor Test', NULL, NULL, NULL, NULL, NULL, NULL, NULL, false, 1, false, NULL, '2026-06-05 14:39:55.311235', NULL, '174142', '2026-06-05 14:49:55.311405', 0, 'Tutor', NULL, true, NULL, NULL, false, NULL, false, NULL);


--
-- TOC entry 5746 (class 0 OID 279620)
-- Dependencies: 294
-- Data for Name: userwarnings; Type: TABLE DATA; Schema: public; Owner: postgres
--



--
-- TOC entry 5748 (class 0 OID 279627)
-- Dependencies: 296
-- Data for Name: wallets; Type: TABLE DATA; Schema: public; Owner: postgres
--

INSERT INTO public.wallets VALUES (25, '162ae810-fa05-44fd-9e46-71a216428851', 0.00, 0.00, '2026-03-05 15:37:43.279033');
INSERT INTO public.wallets VALUES (26, 'c84f0f5b-f7d0-41fc-91ad-f12a4132c8db', 0.00, 0.00, '2026-03-06 11:09:19.505588');
INSERT INTO public.wallets VALUES (28, '48a31ca1-cbfd-49d1-845b-d4a923a2c741', 0.00, 0.00, '2026-03-14 00:59:21.225975');
INSERT INTO public.wallets VALUES (29, '90e466f0-c452-4798-b896-efac322fa1ec', 0.00, 0.00, '2026-03-15 14:49:39.648809');
INSERT INTO public.wallets VALUES (30, 'eb79ec08-d7af-46f3-94cf-a818b2136e10', 0.00, 0.00, '2026-03-16 16:25:17.898933');
INSERT INTO public.wallets VALUES (33, '217ca8dc-8932-497f-b07b-b99af66efba8', 0.00, 0.00, '2026-03-19 05:53:50.126042');
INSERT INTO public.wallets VALUES (35, 'f284548d-85fa-473c-88e0-0b76a08120b1', 0.00, 0.00, '2026-03-20 11:48:22.968395');
INSERT INTO public.wallets VALUES (34, 'e6752adf-18d5-40e3-9dfd-8facb4614695', 0.00, 395200.00, '2026-03-23 10:39:38.906799');


--
-- TOC entry 5750 (class 0 OID 279634)
-- Dependencies: 298
-- Data for Name: wallettransactions; Type: TABLE DATA; Schema: public; Owner: postgres
--

INSERT INTO public.wallettransactions VALUES (63, 34, 380000.00, 'EscrowCredit', 64, 'payment', 'poll-64-dep-20260319060230', '2026-03-19 06:02:30.779225', NULL);
INSERT INTO public.wallettransactions VALUES (64, 34, 3800.00, 'EscrowCredit', 65, 'payment', 'poll-65-dep-20260322152035', '2026-03-22 15:20:35.414584', NULL);
INSERT INTO public.wallettransactions VALUES (65, 34, 3800.00, 'EscrowCredit', 65, 'payment', 'poll-65-rem-20260322152240', '2026-03-22 15:22:40.651413', NULL);
INSERT INTO public.wallettransactions VALUES (66, 34, 3800.00, 'EscrowCredit', 68, 'payment', 'poll-68-dep-20260323103239', '2026-03-23 10:32:39.910041', NULL);
INSERT INTO public.wallettransactions VALUES (67, 34, 3800.00, 'EscrowCredit', 68, 'payment', 'poll-68-rem-20260323103938', '2026-03-23 10:39:38.906808', NULL);


--
-- TOC entry 5752 (class 0 OID 279641)
-- Dependencies: 300
-- Data for Name: withdrawal_scores; Type: TABLE DATA; Schema: public; Owner: postgres
--



--
-- TOC entry 5754 (class 0 OID 279651)
-- Dependencies: 302
-- Data for Name: withdrawalrequests; Type: TABLE DATA; Schema: public; Owner: postgres
--



--
-- TOC entry 5860 (class 0 OID 0)
-- Dependencies: 222
-- Name: aggregatedcounter_id_seq; Type: SEQUENCE SET; Schema: hangfire; Owner: postgres
--

SELECT pg_catalog.setval('hangfire.aggregatedcounter_id_seq', 6, true);


--
-- TOC entry 5861 (class 0 OID 0)
-- Dependencies: 224
-- Name: counter_id_seq; Type: SEQUENCE SET; Schema: hangfire; Owner: postgres
--

SELECT pg_catalog.setval('hangfire.counter_id_seq', 6, true);


--
-- TOC entry 5862 (class 0 OID 0)
-- Dependencies: 226
-- Name: hash_id_seq; Type: SEQUENCE SET; Schema: hangfire; Owner: postgres
--

SELECT pg_catalog.setval('hangfire.hash_id_seq', 1, false);


--
-- TOC entry 5863 (class 0 OID 0)
-- Dependencies: 228
-- Name: job_id_seq; Type: SEQUENCE SET; Schema: hangfire; Owner: postgres
--

SELECT pg_catalog.setval('hangfire.job_id_seq', 2, true);


--
-- TOC entry 5864 (class 0 OID 0)
-- Dependencies: 230
-- Name: jobparameter_id_seq; Type: SEQUENCE SET; Schema: hangfire; Owner: postgres
--

SELECT pg_catalog.setval('hangfire.jobparameter_id_seq', 2, true);


--
-- TOC entry 5865 (class 0 OID 0)
-- Dependencies: 232
-- Name: jobqueue_id_seq; Type: SEQUENCE SET; Schema: hangfire; Owner: postgres
--

SELECT pg_catalog.setval('hangfire.jobqueue_id_seq', 2, true);


--
-- TOC entry 5866 (class 0 OID 0)
-- Dependencies: 234
-- Name: list_id_seq; Type: SEQUENCE SET; Schema: hangfire; Owner: postgres
--

SELECT pg_catalog.setval('hangfire.list_id_seq', 1, false);


--
-- TOC entry 5867 (class 0 OID 0)
-- Dependencies: 239
-- Name: set_id_seq; Type: SEQUENCE SET; Schema: hangfire; Owner: postgres
--

SELECT pg_catalog.setval('hangfire.set_id_seq', 1, true);


--
-- TOC entry 5868 (class 0 OID 0)
-- Dependencies: 241
-- Name: state_id_seq; Type: SEQUENCE SET; Schema: hangfire; Owner: postgres
--

SELECT pg_catalog.setval('hangfire.state_id_seq', 7, true);


--
-- TOC entry 5869 (class 0 OID 0)
-- Dependencies: 244
-- Name: bank_change_logs_logid_seq; Type: SEQUENCE SET; Schema: public; Owner: postgres
--

SELECT pg_catalog.setval('public.bank_change_logs_logid_seq', 1, false);


--
-- TOC entry 5870 (class 0 OID 0)
-- Dependencies: 246
-- Name: bookings_bookingid_seq; Type: SEQUENCE SET; Schema: public; Owner: postgres
--

SELECT pg_catalog.setval('public.bookings_bookingid_seq', 69, true);


--
-- TOC entry 5871 (class 0 OID 0)
-- Dependencies: 248
-- Name: chatchannels_channelid_seq; Type: SEQUENCE SET; Schema: public; Owner: postgres
--

SELECT pg_catalog.setval('public.chatchannels_channelid_seq', 34, true);


--
-- TOC entry 5872 (class 0 OID 0)
-- Dependencies: 250
-- Name: chatmessages_messageid_seq; Type: SEQUENCE SET; Schema: public; Owner: postgres
--

SELECT pg_catalog.setval('public.chatmessages_messageid_seq', 148, true);


--
-- TOC entry 5873 (class 0 OID 0)
-- Dependencies: 253
-- Name: classes_classid_seq; Type: SEQUENCE SET; Schema: public; Owner: postgres
--

SELECT pg_catalog.setval('public.classes_classid_seq', 1, false);


--
-- TOC entry 5874 (class 0 OID 0)
-- Dependencies: 255
-- Name: disputes_disputeid_seq; Type: SEQUENCE SET; Schema: public; Owner: postgres
--

SELECT pg_catalog.setval('public.disputes_disputeid_seq', 2, true);


--
-- TOC entry 5875 (class 0 OID 0)
-- Dependencies: 257
-- Name: feedbacks_feedbackid_seq; Type: SEQUENCE SET; Schema: public; Owner: postgres
--

SELECT pg_catalog.setval('public.feedbacks_feedbackid_seq', 17, true);


--
-- TOC entry 5876 (class 0 OID 0)
-- Dependencies: 259
-- Name: fraud_logs_logid_seq; Type: SEQUENCE SET; Schema: public; Owner: postgres
--

SELECT pg_catalog.setval('public.fraud_logs_logid_seq', 6, true);


--
-- TOC entry 5877 (class 0 OID 0)
-- Dependencies: 305
-- Name: gradelevels_gradelevelid_seq; Type: SEQUENCE SET; Schema: public; Owner: postgres
--

SELECT pg_catalog.setval('public.gradelevels_gradelevelid_seq', 12, true);


--
-- TOC entry 5878 (class 0 OID 0)
-- Dependencies: 261
-- Name: handoversummaries_summaryid_seq; Type: SEQUENCE SET; Schema: public; Owner: postgres
--

SELECT pg_catalog.setval('public.handoversummaries_summaryid_seq', 1, false);


--
-- TOC entry 5879 (class 0 OID 0)
-- Dependencies: 263
-- Name: learningmaterials_materialid_seq; Type: SEQUENCE SET; Schema: public; Owner: postgres
--

SELECT pg_catalog.setval('public.learningmaterials_materialid_seq', 1, false);


--
-- TOC entry 5880 (class 0 OID 0)
-- Dependencies: 265
-- Name: lessonreports_reportid_seq; Type: SEQUENCE SET; Schema: public; Owner: postgres
--

SELECT pg_catalog.setval('public.lessonreports_reportid_seq', 10, true);


--
-- TOC entry 5881 (class 0 OID 0)
-- Dependencies: 267
-- Name: lessons_lessonid_seq; Type: SEQUENCE SET; Schema: public; Owner: postgres
--

SELECT pg_catalog.setval('public.lessons_lessonid_seq', 86, true);


--
-- TOC entry 5882 (class 0 OID 0)
-- Dependencies: 269
-- Name: login_history_logid_seq; Type: SEQUENCE SET; Schema: public; Owner: postgres
--

SELECT pg_catalog.setval('public.login_history_logid_seq', 1, false);


--
-- TOC entry 5883 (class 0 OID 0)
-- Dependencies: 271
-- Name: notifications_notificationid_seq; Type: SEQUENCE SET; Schema: public; Owner: postgres
--

SELECT pg_catalog.setval('public.notifications_notificationid_seq', 174, true);


--
-- TOC entry 5884 (class 0 OID 0)
-- Dependencies: 273
-- Name: profilesuspensions_suspensionid_seq; Type: SEQUENCE SET; Schema: public; Owner: postgres
--

SELECT pg_catalog.setval('public.profilesuspensions_suspensionid_seq', 1, false);


--
-- TOC entry 5885 (class 0 OID 0)
-- Dependencies: 275
-- Name: promotions_promotionid_seq; Type: SEQUENCE SET; Schema: public; Owner: postgres
--

SELECT pg_catalog.setval('public.promotions_promotionid_seq', 1, true);


--
-- TOC entry 5886 (class 0 OID 0)
-- Dependencies: 277
-- Name: studentgrades_gradeid_seq; Type: SEQUENCE SET; Schema: public; Owner: postgres
--

SELECT pg_catalog.setval('public.studentgrades_gradeid_seq', 1, false);


--
-- TOC entry 5887 (class 0 OID 0)
-- Dependencies: 280
-- Name: subjects_subjectid_seq; Type: SEQUENCE SET; Schema: public; Owner: postgres
--

SELECT pg_catalog.setval('public.subjects_subjectid_seq', 3, true);


--
-- TOC entry 5888 (class 0 OID 0)
-- Dependencies: 282
-- Name: systemalerts_alertid_seq; Type: SEQUENCE SET; Schema: public; Owner: postgres
--

SELECT pg_catalog.setval('public.systemalerts_alertid_seq', 1, false);


--
-- TOC entry 5889 (class 0 OID 0)
-- Dependencies: 284
-- Name: systemconfigs_configid_seq; Type: SEQUENCE SET; Schema: public; Owner: postgres
--

SELECT pg_catalog.setval('public.systemconfigs_configid_seq', 10, true);


--
-- TOC entry 5890 (class 0 OID 0)
-- Dependencies: 286
-- Name: topuprequests_topuprequestid_seq; Type: SEQUENCE SET; Schema: public; Owner: postgres
--

SELECT pg_catalog.setval('public.topuprequests_topuprequestid_seq', 1, false);


--
-- TOC entry 5891 (class 0 OID 0)
-- Dependencies: 288
-- Name: tutoravailability_availabilityid_seq; Type: SEQUENCE SET; Schema: public; Owner: postgres
--

SELECT pg_catalog.setval('public.tutoravailability_availabilityid_seq', 62, true);


--
-- TOC entry 5892 (class 0 OID 0)
-- Dependencies: 311
-- Name: tutorpackagefixedslots_fixedslotid_seq; Type: SEQUENCE SET; Schema: public; Owner: postgres
--

SELECT pg_catalog.setval('public.tutorpackagefixedslots_fixedslotid_seq', 1, false);


--
-- TOC entry 5893 (class 0 OID 0)
-- Dependencies: 309
-- Name: tutorpackages_packageid_seq; Type: SEQUENCE SET; Schema: public; Owner: postgres
--

SELECT pg_catalog.setval('public.tutorpackages_packageid_seq', 1, true);


--
-- TOC entry 5894 (class 0 OID 0)
-- Dependencies: 307
-- Name: tutorsubjectgradeprices_id_seq; Type: SEQUENCE SET; Schema: public; Owner: postgres
--

SELECT pg_catalog.setval('public.tutorsubjectgradeprices_id_seq', 1, false);


--
-- TOC entry 5895 (class 0 OID 0)
-- Dependencies: 292
-- Name: tutorsubscriptions_subscriptionid_seq; Type: SEQUENCE SET; Schema: public; Owner: postgres
--

SELECT pg_catalog.setval('public.tutorsubscriptions_subscriptionid_seq', 1, false);


--
-- TOC entry 5896 (class 0 OID 0)
-- Dependencies: 295
-- Name: userwarnings_warningid_seq; Type: SEQUENCE SET; Schema: public; Owner: postgres
--

SELECT pg_catalog.setval('public.userwarnings_warningid_seq', 5, true);


--
-- TOC entry 5897 (class 0 OID 0)
-- Dependencies: 297
-- Name: wallets_walletid_seq; Type: SEQUENCE SET; Schema: public; Owner: postgres
--

SELECT pg_catalog.setval('public.wallets_walletid_seq', 35, true);


--
-- TOC entry 5898 (class 0 OID 0)
-- Dependencies: 299
-- Name: wallettransactions_transactionid_seq; Type: SEQUENCE SET; Schema: public; Owner: postgres
--

SELECT pg_catalog.setval('public.wallettransactions_transactionid_seq', 67, true);


--
-- TOC entry 5899 (class 0 OID 0)
-- Dependencies: 301
-- Name: withdrawal_scores_scoreid_seq; Type: SEQUENCE SET; Schema: public; Owner: postgres
--

SELECT pg_catalog.setval('public.withdrawal_scores_scoreid_seq', 1, true);


--
-- TOC entry 5900 (class 0 OID 0)
-- Dependencies: 303
-- Name: withdrawalrequests_withdrawalid_seq; Type: SEQUENCE SET; Schema: public; Owner: postgres
--

SELECT pg_catalog.setval('public.withdrawalrequests_withdrawalid_seq', 1, true);


--
-- TOC entry 5227 (class 2606 OID 279699)
-- Name: aggregatedcounter aggregatedcounter_key_key; Type: CONSTRAINT; Schema: hangfire; Owner: postgres
--

ALTER TABLE ONLY hangfire.aggregatedcounter
    ADD CONSTRAINT aggregatedcounter_key_key UNIQUE (key);


--
-- TOC entry 5229 (class 2606 OID 279701)
-- Name: aggregatedcounter aggregatedcounter_pkey; Type: CONSTRAINT; Schema: hangfire; Owner: postgres
--

ALTER TABLE ONLY hangfire.aggregatedcounter
    ADD CONSTRAINT aggregatedcounter_pkey PRIMARY KEY (id);


--
-- TOC entry 5231 (class 2606 OID 279703)
-- Name: counter counter_pkey; Type: CONSTRAINT; Schema: hangfire; Owner: postgres
--

ALTER TABLE ONLY hangfire.counter
    ADD CONSTRAINT counter_pkey PRIMARY KEY (id);


--
-- TOC entry 5235 (class 2606 OID 279705)
-- Name: hash hash_key_field_key; Type: CONSTRAINT; Schema: hangfire; Owner: postgres
--

ALTER TABLE ONLY hangfire.hash
    ADD CONSTRAINT hash_key_field_key UNIQUE (key, field);


--
-- TOC entry 5237 (class 2606 OID 279707)
-- Name: hash hash_pkey; Type: CONSTRAINT; Schema: hangfire; Owner: postgres
--

ALTER TABLE ONLY hangfire.hash
    ADD CONSTRAINT hash_pkey PRIMARY KEY (id);


--
-- TOC entry 5243 (class 2606 OID 279709)
-- Name: job job_pkey; Type: CONSTRAINT; Schema: hangfire; Owner: postgres
--

ALTER TABLE ONLY hangfire.job
    ADD CONSTRAINT job_pkey PRIMARY KEY (id);


--
-- TOC entry 5246 (class 2606 OID 279711)
-- Name: jobparameter jobparameter_pkey; Type: CONSTRAINT; Schema: hangfire; Owner: postgres
--

ALTER TABLE ONLY hangfire.jobparameter
    ADD CONSTRAINT jobparameter_pkey PRIMARY KEY (id);


--
-- TOC entry 5251 (class 2606 OID 279713)
-- Name: jobqueue jobqueue_pkey; Type: CONSTRAINT; Schema: hangfire; Owner: postgres
--

ALTER TABLE ONLY hangfire.jobqueue
    ADD CONSTRAINT jobqueue_pkey PRIMARY KEY (id);


--
-- TOC entry 5254 (class 2606 OID 279715)
-- Name: list list_pkey; Type: CONSTRAINT; Schema: hangfire; Owner: postgres
--

ALTER TABLE ONLY hangfire.list
    ADD CONSTRAINT list_pkey PRIMARY KEY (id);


--
-- TOC entry 5256 (class 2606 OID 279717)
-- Name: lock lock_resource_key; Type: CONSTRAINT; Schema: hangfire; Owner: postgres
--

ALTER TABLE ONLY hangfire.lock
    ADD CONSTRAINT lock_resource_key UNIQUE (resource);

ALTER TABLE ONLY hangfire.lock REPLICA IDENTITY USING INDEX lock_resource_key;


--
-- TOC entry 5258 (class 2606 OID 279719)
-- Name: schema schema_pkey; Type: CONSTRAINT; Schema: hangfire; Owner: postgres
--

ALTER TABLE ONLY hangfire.schema
    ADD CONSTRAINT schema_pkey PRIMARY KEY (version);


--
-- TOC entry 5260 (class 2606 OID 279721)
-- Name: server server_pkey; Type: CONSTRAINT; Schema: hangfire; Owner: postgres
--

ALTER TABLE ONLY hangfire.server
    ADD CONSTRAINT server_pkey PRIMARY KEY (id);


--
-- TOC entry 5264 (class 2606 OID 279723)
-- Name: set set_key_value_key; Type: CONSTRAINT; Schema: hangfire; Owner: postgres
--

ALTER TABLE ONLY hangfire.set
    ADD CONSTRAINT set_key_value_key UNIQUE (key, value);


--
-- TOC entry 5266 (class 2606 OID 279725)
-- Name: set set_pkey; Type: CONSTRAINT; Schema: hangfire; Owner: postgres
--

ALTER TABLE ONLY hangfire.set
    ADD CONSTRAINT set_pkey PRIMARY KEY (id);


--
-- TOC entry 5269 (class 2606 OID 279727)
-- Name: state state_pkey; Type: CONSTRAINT; Schema: hangfire; Owner: postgres
--

ALTER TABLE ONLY hangfire.state
    ADD CONSTRAINT state_pkey PRIMARY KEY (id);


--
-- TOC entry 5271 (class 2606 OID 279729)
-- Name: __EFMigrationsHistory PK___EFMigrationsHistory; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."__EFMigrationsHistory"
    ADD CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId");


--
-- TOC entry 5273 (class 2606 OID 279731)
-- Name: bank_change_logs bank_change_logs_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.bank_change_logs
    ADD CONSTRAINT bank_change_logs_pkey PRIMARY KEY (logid);


--
-- TOC entry 5275 (class 2606 OID 279733)
-- Name: bookings bookings_paymentcode_key; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.bookings
    ADD CONSTRAINT bookings_paymentcode_key UNIQUE (paymentcode);


--
-- TOC entry 5277 (class 2606 OID 279735)
-- Name: bookings bookings_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.bookings
    ADD CONSTRAINT bookings_pkey PRIMARY KEY (bookingid);


--
-- TOC entry 5285 (class 2606 OID 279737)
-- Name: chatchannels chatchannels_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.chatchannels
    ADD CONSTRAINT chatchannels_pkey PRIMARY KEY (channelid);


--
-- TOC entry 5288 (class 2606 OID 279739)
-- Name: chatmessages chatmessages_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.chatmessages
    ADD CONSTRAINT chatmessages_pkey PRIMARY KEY (messageid);


--
-- TOC entry 5291 (class 2606 OID 279741)
-- Name: chatparticipants chatparticipants_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.chatparticipants
    ADD CONSTRAINT chatparticipants_pkey PRIMARY KEY (channelid, userid);


--
-- TOC entry 5293 (class 2606 OID 279743)
-- Name: classes classes_bookingid_key; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.classes
    ADD CONSTRAINT classes_bookingid_key UNIQUE (bookingid);


--
-- TOC entry 5295 (class 2606 OID 279745)
-- Name: classes classes_classcode_key; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.classes
    ADD CONSTRAINT classes_classcode_key UNIQUE (classcode);


--
-- TOC entry 5297 (class 2606 OID 279747)
-- Name: classes classes_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.classes
    ADD CONSTRAINT classes_pkey PRIMARY KEY (classid);


--
-- TOC entry 5300 (class 2606 OID 279749)
-- Name: disputes disputes_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.disputes
    ADD CONSTRAINT disputes_pkey PRIMARY KEY (disputeid);


--
-- TOC entry 5302 (class 2606 OID 279751)
-- Name: feedbacks feedbacks_bookingid_fromuserid_key; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.feedbacks
    ADD CONSTRAINT feedbacks_bookingid_fromuserid_key UNIQUE (bookingid, fromuserid);


--
-- TOC entry 5304 (class 2606 OID 279753)
-- Name: feedbacks feedbacks_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.feedbacks
    ADD CONSTRAINT feedbacks_pkey PRIMARY KEY (feedbackid);


--
-- TOC entry 5307 (class 2606 OID 279755)
-- Name: fraud_logs fraud_logs_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.fraud_logs
    ADD CONSTRAINT fraud_logs_pkey PRIMARY KEY (logid);


--
-- TOC entry 5439 (class 2606 OID 295572)
-- Name: gradelevels gradelevels_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.gradelevels
    ADD CONSTRAINT gradelevels_pkey PRIMARY KEY (gradelevelid);


--
-- TOC entry 5317 (class 2606 OID 279757)
-- Name: handoversummaries handoversummaries_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.handoversummaries
    ADD CONSTRAINT handoversummaries_pkey PRIMARY KEY (summaryid);


--
-- TOC entry 5320 (class 2606 OID 279759)
-- Name: learningmaterials learningmaterials_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.learningmaterials
    ADD CONSTRAINT learningmaterials_pkey PRIMARY KEY (materialid);


--
-- TOC entry 5322 (class 2606 OID 279761)
-- Name: lessonreports lessonreports_lessonid_key; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.lessonreports
    ADD CONSTRAINT lessonreports_lessonid_key UNIQUE (lessonid);


--
-- TOC entry 5324 (class 2606 OID 279763)
-- Name: lessonreports lessonreports_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.lessonreports
    ADD CONSTRAINT lessonreports_pkey PRIMARY KEY (reportid);


--
-- TOC entry 5331 (class 2606 OID 279765)
-- Name: lessons lessons_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.lessons
    ADD CONSTRAINT lessons_pkey PRIMARY KEY (lessonid);


--
-- TOC entry 5337 (class 2606 OID 279767)
-- Name: login_history login_history_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.login_history
    ADD CONSTRAINT login_history_pkey PRIMARY KEY (logid);


--
-- TOC entry 5340 (class 2606 OID 279769)
-- Name: notifications notifications_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.notifications
    ADD CONSTRAINT notifications_pkey PRIMARY KEY (notificationid);


--
-- TOC entry 5342 (class 2606 OID 279771)
-- Name: profilesuspensions profilesuspensions_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.profilesuspensions
    ADD CONSTRAINT profilesuspensions_pkey PRIMARY KEY (suspensionid);


--
-- TOC entry 5345 (class 2606 OID 279773)
-- Name: promotions promotions_code_key; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.promotions
    ADD CONSTRAINT promotions_code_key UNIQUE (code);


--
-- TOC entry 5347 (class 2606 OID 279775)
-- Name: promotions promotions_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.promotions
    ADD CONSTRAINT promotions_pkey PRIMARY KEY (promotionid);


--
-- TOC entry 5437 (class 2606 OID 280231)
-- Name: refreshtokens refreshtokens_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.refreshtokens
    ADD CONSTRAINT refreshtokens_pkey PRIMARY KEY (id);


--
-- TOC entry 5351 (class 2606 OID 279777)
-- Name: studentgrades studentgrades_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.studentgrades
    ADD CONSTRAINT studentgrades_pkey PRIMARY KEY (gradeid);


--
-- TOC entry 5354 (class 2606 OID 279779)
-- Name: studentprofiles studentprofiles_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.studentprofiles
    ADD CONSTRAINT studentprofiles_pkey PRIMARY KEY (studentid);


--
-- TOC entry 5356 (class 2606 OID 279781)
-- Name: studentprofiles studentprofiles_studentcode_key; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.studentprofiles
    ADD CONSTRAINT studentprofiles_studentcode_key UNIQUE (studentcode);


--
-- TOC entry 5359 (class 2606 OID 279783)
-- Name: subjects subjects_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.subjects
    ADD CONSTRAINT subjects_pkey PRIMARY KEY (subjectid);


--
-- TOC entry 5361 (class 2606 OID 279785)
-- Name: subjects subjects_subjectname_key; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.subjects
    ADD CONSTRAINT subjects_subjectname_key UNIQUE (subjectname);


--
-- TOC entry 5366 (class 2606 OID 279787)
-- Name: systemalerts systemalerts_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.systemalerts
    ADD CONSTRAINT systemalerts_pkey PRIMARY KEY (alertid);


--
-- TOC entry 5368 (class 2606 OID 279789)
-- Name: systemconfigs systemconfigs_configkey_key; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.systemconfigs
    ADD CONSTRAINT systemconfigs_configkey_key UNIQUE (configkey);


--
-- TOC entry 5370 (class 2606 OID 279791)
-- Name: systemconfigs systemconfigs_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.systemconfigs
    ADD CONSTRAINT systemconfigs_pkey PRIMARY KEY (configid);


--
-- TOC entry 5374 (class 2606 OID 279793)
-- Name: topuprequests topuprequests_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.topuprequests
    ADD CONSTRAINT topuprequests_pkey PRIMARY KEY (topuprequestid);


--
-- TOC entry 5377 (class 2606 OID 279795)
-- Name: tutoravailability tutoravailability_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.tutoravailability
    ADD CONSTRAINT tutoravailability_pkey PRIMARY KEY (availabilityid);


--
-- TOC entry 5381 (class 2606 OID 279797)
-- Name: tutorcertificates tutorcertificates_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.tutorcertificates
    ADD CONSTRAINT tutorcertificates_pkey PRIMARY KEY (certificateid);


--
-- TOC entry 5455 (class 2606 OID 295630)
-- Name: tutorpackagefixedslots tutorpackagefixedslots_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.tutorpackagefixedslots
    ADD CONSTRAINT tutorpackagefixedslots_pkey PRIMARY KEY (fixedslotid);


--
-- TOC entry 5452 (class 2606 OID 295615)
-- Name: tutorpackages tutorpackages_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.tutorpackages
    ADD CONSTRAINT tutorpackages_pkey PRIMARY KEY (packageid);


--
-- TOC entry 5386 (class 2606 OID 279799)
-- Name: tutorprofiles tutorprofiles_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.tutorprofiles
    ADD CONSTRAINT tutorprofiles_pkey PRIMARY KEY (tutorid);


--
-- TOC entry 5447 (class 2606 OID 295586)
-- Name: tutorsubjectgradeprices tutorsubjectgradeprices_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.tutorsubjectgradeprices
    ADD CONSTRAINT tutorsubjectgradeprices_pkey PRIMARY KEY (id);


--
-- TOC entry 5388 (class 2606 OID 279805)
-- Name: tutorsubscriptions tutorsubscriptions_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.tutorsubscriptions
    ADD CONSTRAINT tutorsubscriptions_pkey PRIMARY KEY (subscriptionid);


--
-- TOC entry 5441 (class 2606 OID 295576)
-- Name: gradelevels uq_gradelevels_gradename; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.gradelevels
    ADD CONSTRAINT uq_gradelevels_gradename UNIQUE (gradename);


--
-- TOC entry 5443 (class 2606 OID 295574)
-- Name: gradelevels uq_gradelevels_levelorder; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.gradelevels
    ADD CONSTRAINT uq_gradelevels_levelorder UNIQUE (levelorder);


--
-- TOC entry 5449 (class 2606 OID 295588)
-- Name: tutorsubjectgradeprices uq_tutorsubjectgradeprices_tutor_subject_grade; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.tutorsubjectgradeprices
    ADD CONSTRAINT uq_tutorsubjectgradeprices_tutor_subject_grade UNIQUE (tutorid, subjectid, gradelevelid);


--
-- TOC entry 5392 (class 2606 OID 279807)
-- Name: users users_email_key; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.users
    ADD CONSTRAINT users_email_key UNIQUE (email);


--
-- TOC entry 5394 (class 2606 OID 279809)
-- Name: users users_identitynumber_key; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.users
    ADD CONSTRAINT users_identitynumber_key UNIQUE (identitynumber);


--
-- TOC entry 5397 (class 2606 OID 279811)
-- Name: users users_phone_key; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.users
    ADD CONSTRAINT users_phone_key UNIQUE (phone);


--
-- TOC entry 5399 (class 2606 OID 279813)
-- Name: users users_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.users
    ADD CONSTRAINT users_pkey PRIMARY KEY (userid);


--
-- TOC entry 5401 (class 2606 OID 279815)
-- Name: users users_username_key; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.users
    ADD CONSTRAINT users_username_key UNIQUE (username);


--
-- TOC entry 5403 (class 2606 OID 279817)
-- Name: users users_zalouserid_key; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.users
    ADD CONSTRAINT users_zalouserid_key UNIQUE (zalouserid);


--
-- TOC entry 5406 (class 2606 OID 279819)
-- Name: userwarnings userwarnings_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.userwarnings
    ADD CONSTRAINT userwarnings_pkey PRIMARY KEY (warningid);


--
-- TOC entry 5408 (class 2606 OID 279821)
-- Name: wallets wallets_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.wallets
    ADD CONSTRAINT wallets_pkey PRIMARY KEY (walletid);


--
-- TOC entry 5410 (class 2606 OID 279823)
-- Name: wallets wallets_userid_key; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.wallets
    ADD CONSTRAINT wallets_userid_key UNIQUE (userid);


--
-- TOC entry 5414 (class 2606 OID 279825)
-- Name: wallettransactions wallettransactions_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.wallettransactions
    ADD CONSTRAINT wallettransactions_pkey PRIMARY KEY (transactionid);


--
-- TOC entry 5426 (class 2606 OID 279827)
-- Name: withdrawal_scores withdrawal_scores_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.withdrawal_scores
    ADD CONSTRAINT withdrawal_scores_pkey PRIMARY KEY (scoreid);


--
-- TOC entry 5431 (class 2606 OID 279829)
-- Name: withdrawalrequests withdrawalrequests_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.withdrawalrequests
    ADD CONSTRAINT withdrawalrequests_pkey PRIMARY KEY (withdrawalid);


--
-- TOC entry 5232 (class 1259 OID 279830)
-- Name: ix_hangfire_counter_expireat; Type: INDEX; Schema: hangfire; Owner: postgres
--

CREATE INDEX ix_hangfire_counter_expireat ON hangfire.counter USING btree (expireat);


--
-- TOC entry 5233 (class 1259 OID 279831)
-- Name: ix_hangfire_counter_key; Type: INDEX; Schema: hangfire; Owner: postgres
--

CREATE INDEX ix_hangfire_counter_key ON hangfire.counter USING btree (key);


--
-- TOC entry 5238 (class 1259 OID 279832)
-- Name: ix_hangfire_hash_expireat; Type: INDEX; Schema: hangfire; Owner: postgres
--

CREATE INDEX ix_hangfire_hash_expireat ON hangfire.hash USING btree (expireat);


--
-- TOC entry 5239 (class 1259 OID 279833)
-- Name: ix_hangfire_job_expireat; Type: INDEX; Schema: hangfire; Owner: postgres
--

CREATE INDEX ix_hangfire_job_expireat ON hangfire.job USING btree (expireat);


--
-- TOC entry 5240 (class 1259 OID 279834)
-- Name: ix_hangfire_job_statename; Type: INDEX; Schema: hangfire; Owner: postgres
--

CREATE INDEX ix_hangfire_job_statename ON hangfire.job USING btree (statename);


--
-- TOC entry 5241 (class 1259 OID 279835)
-- Name: ix_hangfire_job_statename_is_not_null; Type: INDEX; Schema: hangfire; Owner: postgres
--

CREATE INDEX ix_hangfire_job_statename_is_not_null ON hangfire.job USING btree (statename) INCLUDE (id) WHERE (statename IS NOT NULL);


--
-- TOC entry 5244 (class 1259 OID 279836)
-- Name: ix_hangfire_jobparameter_jobidandname; Type: INDEX; Schema: hangfire; Owner: postgres
--

CREATE INDEX ix_hangfire_jobparameter_jobidandname ON hangfire.jobparameter USING btree (jobid, name);


--
-- TOC entry 5247 (class 1259 OID 279837)
-- Name: ix_hangfire_jobqueue_fetchedat_queue_jobid; Type: INDEX; Schema: hangfire; Owner: postgres
--

CREATE INDEX ix_hangfire_jobqueue_fetchedat_queue_jobid ON hangfire.jobqueue USING btree (fetchedat NULLS FIRST, queue, jobid);


--
-- TOC entry 5248 (class 1259 OID 279838)
-- Name: ix_hangfire_jobqueue_jobidandqueue; Type: INDEX; Schema: hangfire; Owner: postgres
--

CREATE INDEX ix_hangfire_jobqueue_jobidandqueue ON hangfire.jobqueue USING btree (jobid, queue);


--
-- TOC entry 5249 (class 1259 OID 279839)
-- Name: ix_hangfire_jobqueue_queueandfetchedat; Type: INDEX; Schema: hangfire; Owner: postgres
--

CREATE INDEX ix_hangfire_jobqueue_queueandfetchedat ON hangfire.jobqueue USING btree (queue, fetchedat);


--
-- TOC entry 5252 (class 1259 OID 279840)
-- Name: ix_hangfire_list_expireat; Type: INDEX; Schema: hangfire; Owner: postgres
--

CREATE INDEX ix_hangfire_list_expireat ON hangfire.list USING btree (expireat);


--
-- TOC entry 5261 (class 1259 OID 279841)
-- Name: ix_hangfire_set_expireat; Type: INDEX; Schema: hangfire; Owner: postgres
--

CREATE INDEX ix_hangfire_set_expireat ON hangfire.set USING btree (expireat);


--
-- TOC entry 5262 (class 1259 OID 279842)
-- Name: ix_hangfire_set_key_score; Type: INDEX; Schema: hangfire; Owner: postgres
--

CREATE INDEX ix_hangfire_set_key_score ON hangfire.set USING btree (key, score);


--
-- TOC entry 5267 (class 1259 OID 279843)
-- Name: ix_hangfire_state_jobid; Type: INDEX; Schema: hangfire; Owner: postgres
--

CREATE INDEX ix_hangfire_state_jobid ON hangfire.state USING btree (jobid);


--
-- TOC entry 5278 (class 1259 OID 295670)
-- Name: idx_bookings_packageid; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_bookings_packageid ON public.bookings USING btree (packageid);


--
-- TOC entry 5279 (class 1259 OID 295667)
-- Name: idx_bookings_parentid; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_bookings_parentid ON public.bookings USING btree (parentid);


--
-- TOC entry 5280 (class 1259 OID 279844)
-- Name: idx_bookings_status; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_bookings_status ON public.bookings USING btree (status);


--
-- TOC entry 5281 (class 1259 OID 295668)
-- Name: idx_bookings_studentid; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_bookings_studentid ON public.bookings USING btree (studentid);


--
-- TOC entry 5282 (class 1259 OID 295669)
-- Name: idx_bookings_tutorid; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_bookings_tutorid ON public.bookings USING btree (tutorid);


--
-- TOC entry 5283 (class 1259 OID 295671)
-- Name: idx_bookings_tutorsubjectgradepriceid; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_bookings_tutorsubjectgradepriceid ON public.bookings USING btree (tutorsubjectgradepriceid);


--
-- TOC entry 5289 (class 1259 OID 279845)
-- Name: idx_chatmessages_isread; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_chatmessages_isread ON public.chatmessages USING btree (channelid, isread) WHERE (isread = false);


--
-- TOC entry 5298 (class 1259 OID 279846)
-- Name: idx_classes_code; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_classes_code ON public.classes USING btree (classcode);


--
-- TOC entry 5305 (class 1259 OID 279847)
-- Name: idx_feedbacks_success_story; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_feedbacks_success_story ON public.feedbacks USING btree (isvisible, rating) WHERE ((isvisible = true) AND (rating = 5));


--
-- TOC entry 5308 (class 1259 OID 279848)
-- Name: idx_fraud_logs_checkedat; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_fraud_logs_checkedat ON public.fraud_logs USING btree (checkedat DESC);


--
-- TOC entry 5309 (class 1259 OID 279849)
-- Name: idx_fraud_logs_failed; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_fraud_logs_failed ON public.fraud_logs USING btree (tutorid, checkedat DESC) WHERE (passed = false);


--
-- TOC entry 5310 (class 1259 OID 279850)
-- Name: idx_fraud_logs_flagged; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_fraud_logs_flagged ON public.fraud_logs USING btree (tutorid, checkedat DESC) WHERE (isflagged = true);


--
-- TOC entry 5311 (class 1259 OID 279851)
-- Name: idx_fraud_logs_metadata; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_fraud_logs_metadata ON public.fraud_logs USING gin (metadata);


--
-- TOC entry 5312 (class 1259 OID 279852)
-- Name: idx_fraud_logs_rulename; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_fraud_logs_rulename ON public.fraud_logs USING btree (rulename);


--
-- TOC entry 5313 (class 1259 OID 279853)
-- Name: idx_fraud_logs_tutorid; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_fraud_logs_tutorid ON public.fraud_logs USING btree (tutorid);


--
-- TOC entry 5314 (class 1259 OID 279854)
-- Name: idx_fraud_logs_tutorid_checkedat; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_fraud_logs_tutorid_checkedat ON public.fraud_logs USING btree (tutorid, checkedat DESC);


--
-- TOC entry 5315 (class 1259 OID 279855)
-- Name: idx_fraud_logs_withdrawalid; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_fraud_logs_withdrawalid ON public.fraud_logs USING btree (withdrawalrequestid);


--
-- TOC entry 5318 (class 1259 OID 279856)
-- Name: idx_learningmaterials_student; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_learningmaterials_student ON public.learningmaterials USING btree (studentid);


--
-- TOC entry 5325 (class 1259 OID 279857)
-- Name: idx_lessons_attendance; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_lessons_attendance ON public.lessons USING btree (istutorpresent, isstudentpresent);


--
-- TOC entry 5326 (class 1259 OID 279858)
-- Name: idx_lessons_autoreport; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_lessons_autoreport ON public.lessons USING btree (autoreportsent) WHERE (autoreportsent = false);


--
-- TOC entry 5327 (class 1259 OID 295674)
-- Name: idx_lessons_bookingid; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_lessons_bookingid ON public.lessons USING btree (bookingid);


--
-- TOC entry 5328 (class 1259 OID 295673)
-- Name: idx_lessons_student_schedule; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_lessons_student_schedule ON public.lessons USING btree (studentid, scheduledstart, scheduledend);


--
-- TOC entry 5329 (class 1259 OID 295672)
-- Name: idx_lessons_tutor_schedule; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_lessons_tutor_schedule ON public.lessons USING btree (tutorid, scheduledstart, scheduledend);


--
-- TOC entry 5332 (class 1259 OID 279859)
-- Name: idx_login_history_ipaddress; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_login_history_ipaddress ON public.login_history USING btree (ipaddress);


--
-- TOC entry 5333 (class 1259 OID 279860)
-- Name: idx_login_history_loggedat; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_login_history_loggedat ON public.login_history USING btree (loggedat DESC);


--
-- TOC entry 5334 (class 1259 OID 279861)
-- Name: idx_login_history_userid; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_login_history_userid ON public.login_history USING btree (userid);


--
-- TOC entry 5335 (class 1259 OID 279862)
-- Name: idx_login_history_userid_loggedat; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_login_history_userid_loggedat ON public.login_history USING btree (userid, loggedat DESC);


--
-- TOC entry 5338 (class 1259 OID 279863)
-- Name: idx_notifications_user; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_notifications_user ON public.notifications USING btree (userid, isread);


--
-- TOC entry 5343 (class 1259 OID 279864)
-- Name: idx_promotions_code; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_promotions_code ON public.promotions USING btree (code);


--
-- TOC entry 5432 (class 1259 OID 280240)
-- Name: idx_refreshtokens_expiresat; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_refreshtokens_expiresat ON public.refreshtokens USING btree (expiresat);


--
-- TOC entry 5433 (class 1259 OID 280239)
-- Name: idx_refreshtokens_tokenfamily; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_refreshtokens_tokenfamily ON public.refreshtokens USING btree (tokenfamily);


--
-- TOC entry 5434 (class 1259 OID 280237)
-- Name: idx_refreshtokens_tokenhash; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX idx_refreshtokens_tokenhash ON public.refreshtokens USING btree (tokenhash);


--
-- TOC entry 5435 (class 1259 OID 280238)
-- Name: idx_refreshtokens_userid; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_refreshtokens_userid ON public.refreshtokens USING btree (userid);


--
-- TOC entry 5348 (class 1259 OID 279865)
-- Name: idx_studentgrades_booking; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_studentgrades_booking ON public.studentgrades USING btree (bookingid);


--
-- TOC entry 5349 (class 1259 OID 279866)
-- Name: idx_studentgrades_student; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_studentgrades_student ON public.studentgrades USING btree (studentid);


--
-- TOC entry 5352 (class 1259 OID 279867)
-- Name: idx_studentprofiles_code; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_studentprofiles_code ON public.studentprofiles USING btree (studentcode);


--
-- TOC entry 5357 (class 1259 OID 279868)
-- Name: idx_subjects_name_fuzzy; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_subjects_name_fuzzy ON public.subjects USING gin (public.immutable_unaccent((subjectname)::text) public.gin_trgm_ops);


--
-- TOC entry 5362 (class 1259 OID 279869)
-- Name: idx_systemalerts_createdat; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_systemalerts_createdat ON public.systemalerts USING btree (createdat DESC);


--
-- TOC entry 5363 (class 1259 OID 279870)
-- Name: idx_systemalerts_severity_createdat; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_systemalerts_severity_createdat ON public.systemalerts USING btree (severity, createdat DESC);


--
-- TOC entry 5364 (class 1259 OID 279871)
-- Name: idx_systemalerts_type_resolved; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_systemalerts_type_resolved ON public.systemalerts USING btree (type, resolved);


--
-- TOC entry 5371 (class 1259 OID 279872)
-- Name: idx_topuprequests_ordercode; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX idx_topuprequests_ordercode ON public.topuprequests USING btree (ordercode);


--
-- TOC entry 5372 (class 1259 OID 279873)
-- Name: idx_topuprequests_userid_status; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_topuprequests_userid_status ON public.topuprequests USING btree (userid, status);


--
-- TOC entry 5375 (class 1259 OID 295664)
-- Name: idx_tutoravailability_tutor_day_time; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_tutoravailability_tutor_day_time ON public.tutoravailability USING btree (tutorid, dayofweek, starttime, endtime);


--
-- TOC entry 5378 (class 1259 OID 279874)
-- Name: idx_tutorcertificate_status; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_tutorcertificate_status ON public.tutorcertificates USING btree (verificationstatus);


--
-- TOC entry 5379 (class 1259 OID 279875)
-- Name: idx_tutorcertificate_tutorid; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_tutorcertificate_tutorid ON public.tutorcertificates USING btree (tutorid);


--
-- TOC entry 5453 (class 1259 OID 295666)
-- Name: idx_tutorpackagefixedslots_package_day; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_tutorpackagefixedslots_package_day ON public.tutorpackagefixedslots USING btree (packageid, dayofweek);


--
-- TOC entry 5450 (class 1259 OID 295665)
-- Name: idx_tutorpackages_tutor_active_type; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_tutorpackages_tutor_active_type ON public.tutorpackages USING btree (tutorid, isactive, packagetype);


--
-- TOC entry 5382 (class 1259 OID 279876)
-- Name: idx_tutorprofiles_bankverified; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_tutorprofiles_bankverified ON public.tutorprofiles USING btree (isbankverified) WHERE (isbankverified = true);


--
-- TOC entry 5383 (class 1259 OID 279878)
-- Name: idx_tutorprofiles_headline_fuzzy; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_tutorprofiles_headline_fuzzy ON public.tutorprofiles USING gin (public.immutable_unaccent((headline)::text) public.gin_trgm_ops);


--
-- TOC entry 5384 (class 1259 OID 279879)
-- Name: idx_tutorprofiles_reviewedat; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_tutorprofiles_reviewedat ON public.tutorprofiles USING btree (reviewedat);


--
-- TOC entry 5444 (class 1259 OID 295663)
-- Name: idx_tutorsubjectgradeprices_tutor_active; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_tutorsubjectgradeprices_tutor_active ON public.tutorsubjectgradeprices USING btree (tutorid, isactive);


--
-- TOC entry 5445 (class 1259 OID 295662)
-- Name: idx_tutorsubjectgradeprices_tutor_subject_grade; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_tutorsubjectgradeprices_tutor_subject_grade ON public.tutorsubjectgradeprices USING btree (tutorid, subjectid, gradelevelid);


--
-- TOC entry 5389 (class 1259 OID 279880)
-- Name: idx_users_email; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_users_email ON public.users USING btree (email);


--
-- TOC entry 5390 (class 1259 OID 279881)
-- Name: idx_users_fullname_fuzzy; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_users_fullname_fuzzy ON public.users USING gin (public.immutable_unaccent((fullname)::text) public.gin_trgm_ops);


--
-- TOC entry 5404 (class 1259 OID 279882)
-- Name: idx_userwarnings_user; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_userwarnings_user ON public.userwarnings USING btree (userid);


--
-- TOC entry 5411 (class 1259 OID 279883)
-- Name: idx_wallettransactions_ordercode; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_wallettransactions_ordercode ON public.wallettransactions USING btree (ordercode) WHERE (ordercode IS NOT NULL);


--
-- TOC entry 5412 (class 1259 OID 279884)
-- Name: idx_wallettransactions_walletid_createdat; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_wallettransactions_walletid_createdat ON public.wallettransactions USING btree (walletid, createdat DESC);


--
-- TOC entry 5415 (class 1259 OID 279885)
-- Name: idx_withdrawal_scores_auto_reject; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_withdrawal_scores_auto_reject ON public.withdrawal_scores USING btree (tutorid, createdat DESC) WHERE ((decision)::text = 'AUTO_REJECT'::text);


--
-- TOC entry 5416 (class 1259 OID 279886)
-- Name: idx_withdrawal_scores_createdat; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_withdrawal_scores_createdat ON public.withdrawal_scores USING btree (createdat DESC);


--
-- TOC entry 5417 (class 1259 OID 279887)
-- Name: idx_withdrawal_scores_decision; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_withdrawal_scores_decision ON public.withdrawal_scores USING btree (decision);


--
-- TOC entry 5418 (class 1259 OID 279888)
-- Name: idx_withdrawal_scores_fraudflags; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_withdrawal_scores_fraudflags ON public.withdrawal_scores USING gin (fraudflags);


--
-- TOC entry 5419 (class 1259 OID 279889)
-- Name: idx_withdrawal_scores_manual_review; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_withdrawal_scores_manual_review ON public.withdrawal_scores USING btree (tutorid, createdat DESC) WHERE ((decision)::text = 'MANUAL_REVIEW'::text);


--
-- TOC entry 5420 (class 1259 OID 279890)
-- Name: idx_withdrawal_scores_negativefactors; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_withdrawal_scores_negativefactors ON public.withdrawal_scores USING gin (negativefactors);


--
-- TOC entry 5421 (class 1259 OID 279891)
-- Name: idx_withdrawal_scores_positivefactors; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_withdrawal_scores_positivefactors ON public.withdrawal_scores USING gin (positivefactors);


--
-- TOC entry 5422 (class 1259 OID 279892)
-- Name: idx_withdrawal_scores_tutorid; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_withdrawal_scores_tutorid ON public.withdrawal_scores USING btree (tutorid);


--
-- TOC entry 5423 (class 1259 OID 279893)
-- Name: idx_withdrawal_scores_tutorid_createdat; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_withdrawal_scores_tutorid_createdat ON public.withdrawal_scores USING btree (tutorid, createdat DESC);


--
-- TOC entry 5424 (class 1259 OID 279894)
-- Name: idx_withdrawal_scores_withdrawalid; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_withdrawal_scores_withdrawalid ON public.withdrawal_scores USING btree (withdrawalrequestid);


--
-- TOC entry 5427 (class 1259 OID 279895)
-- Name: idx_withdrawalrequests_decision; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_withdrawalrequests_decision ON public.withdrawalrequests USING btree (decision) WHERE (decision IS NOT NULL);


--
-- TOC entry 5428 (class 1259 OID 279896)
-- Name: idx_withdrawalrequests_payostransactionid; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_withdrawalrequests_payostransactionid ON public.withdrawalrequests USING btree (payostransactionid);


--
-- TOC entry 5429 (class 1259 OID 279897)
-- Name: idx_withdrawalrequests_status_retrycount; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_withdrawalrequests_status_retrycount ON public.withdrawalrequests USING btree (status, retrycount) WHERE ((status)::text = 'Pending'::text);


--
-- TOC entry 5286 (class 1259 OID 279898)
-- Name: ix_chatchannels_parentid_tutorid; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX ix_chatchannels_parentid_tutorid ON public.chatchannels USING btree (parentid, tutorid) WHERE ((parentid IS NOT NULL) AND (tutorid IS NOT NULL));


--
-- TOC entry 5395 (class 1259 OID 279899)
-- Name: users_parentcode_key; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX users_parentcode_key ON public.users USING btree (parentcode) WHERE (parentcode IS NOT NULL);


--
-- TOC entry 5456 (class 2606 OID 279900)
-- Name: jobparameter jobparameter_jobid_fkey; Type: FK CONSTRAINT; Schema: hangfire; Owner: postgres
--

ALTER TABLE ONLY hangfire.jobparameter
    ADD CONSTRAINT jobparameter_jobid_fkey FOREIGN KEY (jobid) REFERENCES hangfire.job(id) ON UPDATE CASCADE ON DELETE CASCADE;


--
-- TOC entry 5457 (class 2606 OID 279905)
-- Name: state state_jobid_fkey; Type: FK CONSTRAINT; Schema: hangfire; Owner: postgres
--

ALTER TABLE ONLY hangfire.state
    ADD CONSTRAINT state_jobid_fkey FOREIGN KEY (jobid) REFERENCES hangfire.job(id) ON UPDATE CASCADE ON DELETE CASCADE;


--
-- TOC entry 5458 (class 2606 OID 279910)
-- Name: bookings bookings_parentid_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.bookings
    ADD CONSTRAINT bookings_parentid_fkey FOREIGN KEY (parentid) REFERENCES public.users(userid);


--
-- TOC entry 5459 (class 2606 OID 279915)
-- Name: bookings bookings_promotionid_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.bookings
    ADD CONSTRAINT bookings_promotionid_fkey FOREIGN KEY (promotionid) REFERENCES public.promotions(promotionid);


--
-- TOC entry 5460 (class 2606 OID 279920)
-- Name: bookings bookings_studentid_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.bookings
    ADD CONSTRAINT bookings_studentid_fkey FOREIGN KEY (studentid) REFERENCES public.studentprofiles(studentid) ON DELETE CASCADE;


--
-- TOC entry 5461 (class 2606 OID 279930)
-- Name: bookings bookings_tutorid_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.bookings
    ADD CONSTRAINT bookings_tutorid_fkey FOREIGN KEY (tutorid) REFERENCES public.tutorprofiles(tutorid);


--
-- TOC entry 5464 (class 2606 OID 279935)
-- Name: chatchannels chatchannels_bookingid_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.chatchannels
    ADD CONSTRAINT chatchannels_bookingid_fkey FOREIGN KEY (bookingid) REFERENCES public.bookings(bookingid) ON DELETE SET NULL;


--
-- TOC entry 5465 (class 2606 OID 279940)
-- Name: chatchannels chatchannels_parentid_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.chatchannels
    ADD CONSTRAINT chatchannels_parentid_fkey FOREIGN KEY (parentid) REFERENCES public.users(userid) ON DELETE SET NULL;


--
-- TOC entry 5466 (class 2606 OID 279945)
-- Name: chatchannels chatchannels_tutorid_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.chatchannels
    ADD CONSTRAINT chatchannels_tutorid_fkey FOREIGN KEY (tutorid) REFERENCES public.users(userid) ON DELETE SET NULL;


--
-- TOC entry 5467 (class 2606 OID 279950)
-- Name: chatmessages chatmessages_channelid_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.chatmessages
    ADD CONSTRAINT chatmessages_channelid_fkey FOREIGN KEY (channelid) REFERENCES public.chatchannels(channelid) ON DELETE CASCADE;


--
-- TOC entry 5468 (class 2606 OID 279955)
-- Name: chatmessages chatmessages_senderid_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.chatmessages
    ADD CONSTRAINT chatmessages_senderid_fkey FOREIGN KEY (senderid) REFERENCES public.users(userid);


--
-- TOC entry 5469 (class 2606 OID 279960)
-- Name: chatparticipants chatparticipants_channelid_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.chatparticipants
    ADD CONSTRAINT chatparticipants_channelid_fkey FOREIGN KEY (channelid) REFERENCES public.chatchannels(channelid) ON DELETE CASCADE;


--
-- TOC entry 5470 (class 2606 OID 279965)
-- Name: chatparticipants chatparticipants_userid_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.chatparticipants
    ADD CONSTRAINT chatparticipants_userid_fkey FOREIGN KEY (userid) REFERENCES public.users(userid) ON DELETE CASCADE;


--
-- TOC entry 5471 (class 2606 OID 279970)
-- Name: classes classes_bookingid_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.classes
    ADD CONSTRAINT classes_bookingid_fkey FOREIGN KEY (bookingid) REFERENCES public.bookings(bookingid) ON DELETE CASCADE;


--
-- TOC entry 5472 (class 2606 OID 279975)
-- Name: classes classes_tutorid_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.classes
    ADD CONSTRAINT classes_tutorid_fkey FOREIGN KEY (tutorid) REFERENCES public.tutorprofiles(tutorid);


--
-- TOC entry 5473 (class 2606 OID 279980)
-- Name: disputes disputes_bookingid_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.disputes
    ADD CONSTRAINT disputes_bookingid_fkey FOREIGN KEY (bookingid) REFERENCES public.bookings(bookingid);


--
-- TOC entry 5474 (class 2606 OID 279985)
-- Name: disputes disputes_createdby_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.disputes
    ADD CONSTRAINT disputes_createdby_fkey FOREIGN KEY (createdby) REFERENCES public.users(userid);


--
-- TOC entry 5475 (class 2606 OID 279990)
-- Name: disputes disputes_lessonid_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.disputes
    ADD CONSTRAINT disputes_lessonid_fkey FOREIGN KEY (lessonid) REFERENCES public.lessons(lessonid);


--
-- TOC entry 5476 (class 2606 OID 279995)
-- Name: disputes disputes_resolvedby_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.disputes
    ADD CONSTRAINT disputes_resolvedby_fkey FOREIGN KEY (resolvedby) REFERENCES public.users(userid);


--
-- TOC entry 5477 (class 2606 OID 280000)
-- Name: feedbacks feedbacks_bookingid_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.feedbacks
    ADD CONSTRAINT feedbacks_bookingid_fkey FOREIGN KEY (bookingid) REFERENCES public.bookings(bookingid);


--
-- TOC entry 5478 (class 2606 OID 280005)
-- Name: feedbacks feedbacks_fromuserid_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.feedbacks
    ADD CONSTRAINT feedbacks_fromuserid_fkey FOREIGN KEY (fromuserid) REFERENCES public.users(userid);


--
-- TOC entry 5479 (class 2606 OID 280010)
-- Name: feedbacks feedbacks_lessonid_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.feedbacks
    ADD CONSTRAINT feedbacks_lessonid_fkey FOREIGN KEY (lessonid) REFERENCES public.lessons(lessonid);


--
-- TOC entry 5480 (class 2606 OID 280015)
-- Name: feedbacks feedbacks_touserid_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.feedbacks
    ADD CONSTRAINT feedbacks_touserid_fkey FOREIGN KEY (touserid) REFERENCES public.users(userid);


--
-- TOC entry 5462 (class 2606 OID 295652)
-- Name: bookings fk_bookings_package; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.bookings
    ADD CONSTRAINT fk_bookings_package FOREIGN KEY (packageid) REFERENCES public.tutorpackages(packageid);


--
-- TOC entry 5463 (class 2606 OID 295647)
-- Name: bookings fk_bookings_tutorsubjectgradeprice; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.bookings
    ADD CONSTRAINT fk_bookings_tutorsubjectgradeprice FOREIGN KEY (tutorsubjectgradepriceid) REFERENCES public.tutorsubjectgradeprices(id);


--
-- TOC entry 5481 (class 2606 OID 280020)
-- Name: fraud_logs fk_fraud_logs_tutor; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.fraud_logs
    ADD CONSTRAINT fk_fraud_logs_tutor FOREIGN KEY (tutorid) REFERENCES public.tutorprofiles(tutorid) ON DELETE CASCADE;


--
-- TOC entry 5482 (class 2606 OID 280025)
-- Name: fraud_logs fk_fraud_logs_withdrawal; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.fraud_logs
    ADD CONSTRAINT fk_fraud_logs_withdrawal FOREIGN KEY (withdrawalrequestid) REFERENCES public.withdrawalrequests(withdrawalid) ON DELETE SET NULL;




--
-- TOC entry 5497 (class 2606 OID 280030)
-- Name: login_history fk_login_history_user; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.login_history
    ADD CONSTRAINT fk_login_history_user FOREIGN KEY (userid) REFERENCES public.users(userid) ON DELETE CASCADE;


--
-- TOC entry 5522 (class 2606 OID 280232)
-- Name: refreshtokens fk_refreshtokens_user; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.refreshtokens
    ADD CONSTRAINT fk_refreshtokens_user FOREIGN KEY (userid) REFERENCES public.users(userid) ON DELETE CASCADE;


--
-- TOC entry 5505 (class 2606 OID 295638)
-- Name: studentprofiles fk_studentprofiles_gradelevel; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.studentprofiles
    ADD CONSTRAINT fk_studentprofiles_gradelevel FOREIGN KEY (gradelevelid) REFERENCES public.gradelevels(gradelevelid);


--
-- TOC entry 5509 (class 2606 OID 280035)
-- Name: topuprequests fk_topuprequests_userid; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.topuprequests
    ADD CONSTRAINT fk_topuprequests_userid FOREIGN KEY (userid) REFERENCES public.users(userid) ON DELETE CASCADE;


--
-- TOC entry 5511 (class 2606 OID 280040)
-- Name: tutorcertificates fk_tutorcertificate_tutor; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.tutorcertificates
    ADD CONSTRAINT fk_tutorcertificate_tutor FOREIGN KEY (tutorid) REFERENCES public.tutorprofiles(tutorid) ON DELETE CASCADE;


--
-- TOC entry 5527 (class 2606 OID 295631)
-- Name: tutorpackagefixedslots fk_tutorpackagefixedslots_package; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.tutorpackagefixedslots
    ADD CONSTRAINT fk_tutorpackagefixedslots_package FOREIGN KEY (packageid) REFERENCES public.tutorpackages(packageid) ON DELETE CASCADE;


--
-- TOC entry 5526 (class 2606 OID 295616)
-- Name: tutorpackages fk_tutorpackages_tutor; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.tutorpackages
    ADD CONSTRAINT fk_tutorpackages_tutor FOREIGN KEY (tutorid) REFERENCES public.tutorprofiles(tutorid) ON DELETE CASCADE;


--
-- TOC entry 5523 (class 2606 OID 295600)
-- Name: tutorsubjectgradeprices fk_tutorsubjectgradeprices_gradelevel; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.tutorsubjectgradeprices
    ADD CONSTRAINT fk_tutorsubjectgradeprices_gradelevel FOREIGN KEY (gradelevelid) REFERENCES public.gradelevels(gradelevelid);


--
-- TOC entry 5524 (class 2606 OID 295595)
-- Name: tutorsubjectgradeprices fk_tutorsubjectgradeprices_subject; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.tutorsubjectgradeprices
    ADD CONSTRAINT fk_tutorsubjectgradeprices_subject FOREIGN KEY (subjectid) REFERENCES public.subjects(subjectid) ON DELETE CASCADE;


--
-- TOC entry 5525 (class 2606 OID 295590)
-- Name: tutorsubjectgradeprices fk_tutorsubjectgradeprices_tutor; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.tutorsubjectgradeprices
    ADD CONSTRAINT fk_tutorsubjectgradeprices_tutor FOREIGN KEY (tutorid) REFERENCES public.tutorprofiles(tutorid) ON DELETE CASCADE;


--
-- TOC entry 5519 (class 2606 OID 280045)
-- Name: withdrawal_scores fk_withdrawal_scores_tutor; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.withdrawal_scores
    ADD CONSTRAINT fk_withdrawal_scores_tutor FOREIGN KEY (tutorid) REFERENCES public.tutorprofiles(tutorid) ON DELETE CASCADE;


--
-- TOC entry 5520 (class 2606 OID 280050)
-- Name: withdrawal_scores fk_withdrawal_scores_withdrawal; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.withdrawal_scores
    ADD CONSTRAINT fk_withdrawal_scores_withdrawal FOREIGN KEY (withdrawalrequestid) REFERENCES public.withdrawalrequests(withdrawalid) ON DELETE CASCADE;


--
-- TOC entry 5483 (class 2606 OID 280055)
-- Name: handoversummaries handoversummaries_frombookingid_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.handoversummaries
    ADD CONSTRAINT handoversummaries_frombookingid_fkey FOREIGN KEY (frombookingid) REFERENCES public.bookings(bookingid);


--
-- TOC entry 5484 (class 2606 OID 280060)
-- Name: handoversummaries handoversummaries_fromtutorid_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.handoversummaries
    ADD CONSTRAINT handoversummaries_fromtutorid_fkey FOREIGN KEY (fromtutorid) REFERENCES public.tutorprofiles(tutorid);


--
-- TOC entry 5485 (class 2606 OID 280065)
-- Name: handoversummaries handoversummaries_studentid_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.handoversummaries
    ADD CONSTRAINT handoversummaries_studentid_fkey FOREIGN KEY (studentid) REFERENCES public.studentprofiles(studentid) ON DELETE CASCADE;


--
-- TOC entry 5486 (class 2606 OID 280070)
-- Name: handoversummaries handoversummaries_totutorid_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.handoversummaries
    ADD CONSTRAINT handoversummaries_totutorid_fkey FOREIGN KEY (totutorid) REFERENCES public.tutorprofiles(tutorid);


--
-- TOC entry 5487 (class 2606 OID 280075)
-- Name: learningmaterials learningmaterials_bookingid_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.learningmaterials
    ADD CONSTRAINT learningmaterials_bookingid_fkey FOREIGN KEY (bookingid) REFERENCES public.bookings(bookingid) ON DELETE SET NULL;


--
-- TOC entry 5488 (class 2606 OID 280080)
-- Name: learningmaterials learningmaterials_studentid_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.learningmaterials
    ADD CONSTRAINT learningmaterials_studentid_fkey FOREIGN KEY (studentid) REFERENCES public.studentprofiles(studentid) ON DELETE CASCADE;


--
-- TOC entry 5489 (class 2606 OID 280085)
-- Name: learningmaterials learningmaterials_uploadedby_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.learningmaterials
    ADD CONSTRAINT learningmaterials_uploadedby_fkey FOREIGN KEY (uploadedby) REFERENCES public.users(userid);


--
-- TOC entry 5490 (class 2606 OID 280090)
-- Name: lessonreports lessonreports_createdbytutorid_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.lessonreports
    ADD CONSTRAINT lessonreports_createdbytutorid_fkey FOREIGN KEY (createdbytutorid) REFERENCES public.tutorprofiles(tutorid);


--
-- TOC entry 5491 (class 2606 OID 280095)
-- Name: lessonreports lessonreports_lessonid_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.lessonreports
    ADD CONSTRAINT lessonreports_lessonid_fkey FOREIGN KEY (lessonid) REFERENCES public.lessons(lessonid) ON DELETE CASCADE;


--
-- TOC entry 5493 (class 2606 OID 280100)
-- Name: lessons lessons_bookingid_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.lessons
    ADD CONSTRAINT lessons_bookingid_fkey FOREIGN KEY (bookingid) REFERENCES public.bookings(bookingid) ON DELETE CASCADE;


--
-- TOC entry 5494 (class 2606 OID 280105)
-- Name: lessons lessons_originalessonid_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.lessons
    ADD CONSTRAINT lessons_originalessonid_fkey FOREIGN KEY (originallessonid) REFERENCES public.lessons(lessonid);


--
-- TOC entry 5495 (class 2606 OID 280110)
-- Name: lessons lessons_studentid_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.lessons
    ADD CONSTRAINT lessons_studentid_fkey FOREIGN KEY (studentid) REFERENCES public.studentprofiles(studentid) ON DELETE CASCADE;


--
-- TOC entry 5496 (class 2606 OID 280115)
-- Name: lessons lessons_tutorid_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.lessons
    ADD CONSTRAINT lessons_tutorid_fkey FOREIGN KEY (tutorid) REFERENCES public.tutorprofiles(tutorid);


--
-- TOC entry 5498 (class 2606 OID 280120)
-- Name: notifications notifications_userid_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.notifications
    ADD CONSTRAINT notifications_userid_fkey FOREIGN KEY (userid) REFERENCES public.users(userid) ON DELETE CASCADE;


--
-- TOC entry 5499 (class 2606 OID 280125)
-- Name: profilesuspensions profilesuspensions_createdby_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.profilesuspensions
    ADD CONSTRAINT profilesuspensions_createdby_fkey FOREIGN KEY (createdby) REFERENCES public.users(userid);


--
-- TOC entry 5500 (class 2606 OID 280130)
-- Name: profilesuspensions profilesuspensions_userid_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.profilesuspensions
    ADD CONSTRAINT profilesuspensions_userid_fkey FOREIGN KEY (userid) REFERENCES public.users(userid) ON DELETE CASCADE;


--
-- TOC entry 5501 (class 2606 OID 280135)
-- Name: studentgrades studentgrades_bookingid_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.studentgrades
    ADD CONSTRAINT studentgrades_bookingid_fkey FOREIGN KEY (bookingid) REFERENCES public.bookings(bookingid) ON DELETE SET NULL;


--
-- TOC entry 5502 (class 2606 OID 280140)
-- Name: studentgrades studentgrades_studentid_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.studentgrades
    ADD CONSTRAINT studentgrades_studentid_fkey FOREIGN KEY (studentid) REFERENCES public.studentprofiles(studentid) ON DELETE CASCADE;


--
-- TOC entry 5503 (class 2606 OID 280145)
-- Name: studentgrades studentgrades_subjectid_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.studentgrades
    ADD CONSTRAINT studentgrades_subjectid_fkey FOREIGN KEY (subjectid) REFERENCES public.subjects(subjectid);


--
-- TOC entry 5504 (class 2606 OID 280150)
-- Name: studentgrades studentgrades_tutorid_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.studentgrades
    ADD CONSTRAINT studentgrades_tutorid_fkey FOREIGN KEY (tutorid) REFERENCES public.tutorprofiles(tutorid);


--
-- TOC entry 5506 (class 2606 OID 280155)
-- Name: studentprofiles studentprofiles_linkeduserid_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.studentprofiles
    ADD CONSTRAINT studentprofiles_linkeduserid_fkey FOREIGN KEY (linkeduserid) REFERENCES public.users(userid) ON DELETE SET NULL;


--
-- TOC entry 5507 (class 2606 OID 280160)
-- Name: studentprofiles studentprofiles_parentid_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.studentprofiles
    ADD CONSTRAINT studentprofiles_parentid_fkey FOREIGN KEY (parentid) REFERENCES public.users(userid) ON DELETE CASCADE;


--
-- TOC entry 5508 (class 2606 OID 280165)
-- Name: systemconfigs systemconfigs_updatedby_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.systemconfigs
    ADD CONSTRAINT systemconfigs_updatedby_fkey FOREIGN KEY (updatedby) REFERENCES public.users(userid);


--
-- TOC entry 5510 (class 2606 OID 280170)
-- Name: tutoravailability tutoravailability_tutorid_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.tutoravailability
    ADD CONSTRAINT tutoravailability_tutorid_fkey FOREIGN KEY (tutorid) REFERENCES public.tutorprofiles(tutorid) ON DELETE CASCADE;


--
-- TOC entry 5512 (class 2606 OID 280175)
-- Name: tutorprofiles tutorprofiles_tutorid_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.tutorprofiles
    ADD CONSTRAINT tutorprofiles_tutorid_fkey FOREIGN KEY (tutorid) REFERENCES public.users(userid) ON DELETE CASCADE;


--
-- TOC entry 5513 (class 2606 OID 280190)
-- Name: tutorsubscriptions tutorsubscriptions_tutorid_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.tutorsubscriptions
    ADD CONSTRAINT tutorsubscriptions_tutorid_fkey FOREIGN KEY (tutorid) REFERENCES public.tutorprofiles(tutorid) ON DELETE CASCADE;


--
-- TOC entry 5514 (class 2606 OID 280195)
-- Name: userwarnings userwarnings_issuedby_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.userwarnings
    ADD CONSTRAINT userwarnings_issuedby_fkey FOREIGN KEY (issuedby) REFERENCES public.users(userid);


--
-- TOC entry 5515 (class 2606 OID 280200)
-- Name: userwarnings userwarnings_relatedbookingid_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.userwarnings
    ADD CONSTRAINT userwarnings_relatedbookingid_fkey FOREIGN KEY (relatedbookingid) REFERENCES public.bookings(bookingid);


--
-- TOC entry 5516 (class 2606 OID 280205)
-- Name: userwarnings userwarnings_userid_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.userwarnings
    ADD CONSTRAINT userwarnings_userid_fkey FOREIGN KEY (userid) REFERENCES public.users(userid) ON DELETE CASCADE;


--
-- TOC entry 5517 (class 2606 OID 280210)
-- Name: wallets wallets_userid_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.wallets
    ADD CONSTRAINT wallets_userid_fkey FOREIGN KEY (userid) REFERENCES public.users(userid);


--
-- TOC entry 5518 (class 2606 OID 280215)
-- Name: wallettransactions wallettransactions_walletid_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.wallettransactions
    ADD CONSTRAINT wallettransactions_walletid_fkey FOREIGN KEY (walletid) REFERENCES public.wallets(walletid);


--
-- TOC entry 5521 (class 2606 OID 280220)
-- Name: withdrawalrequests withdrawalrequests_userid_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.withdrawalrequests
    ADD CONSTRAINT withdrawalrequests_userid_fkey FOREIGN KEY (userid) REFERENCES public.users(userid);


--
-- TOC entry 5771 (class 0 OID 0)
-- Dependencies: 9
-- Name: SCHEMA public; Type: ACL; Schema: -; Owner: postgres
--

REVOKE USAGE ON SCHEMA public FROM PUBLIC;


-- Completed on 2026-06-05 23:20:37

--
-- PostgreSQL database dump complete
--

